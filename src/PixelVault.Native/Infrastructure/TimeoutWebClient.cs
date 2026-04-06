using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    /// <summary>HttpClient wrapper with timeouts and download size limits. Use <see cref="DownloadStringAsync"/> / <see cref="DownloadFileAsync"/> only (no sync wrappers).</summary>
    sealed class TimeoutWebClient : IDisposable
    {
        /// <summary>Default cap for JSON/text responses (Steam store API, SteamGridDB search, etc.). Zero or negative disables the limit.</summary>
        public const long DefaultMaxStringResponseBytes = 4L * 1024 * 1024;

        /// <summary>Default cap for binary downloads (e.g. cover art). Zero or negative disables the limit.</summary>
        public const long DefaultMaxFileDownloadBytes = 48L * 1024 * 1024;

        readonly HttpClientHandler handler = new HttpClientHandler();
        readonly HttpClient client;
        bool disposed;

        public TimeoutWebClient()
        {
            client = new HttpClient(handler, true);
        }

        public Encoding Encoding = Encoding.UTF8;
        public int TimeoutMilliseconds = 15000;
        public WebHeaderCollection Headers = new WebHeaderCollection();

        /// <summary>Maximum response body size for <see cref="DownloadStringAsync"/>. Zero or negative means no limit.</summary>
        public long MaxStringResponseBytes = DefaultMaxStringResponseBytes;

        /// <summary>Maximum bytes written for <see cref="DownloadFileAsync"/>. Zero or negative means no limit.</summary>
        public long MaxFileDownloadBytes = DefaultMaxFileDownloadBytes;

        HttpRequestMessage BuildRequest(HttpMethod method, string address)
        {
            var request = new HttpRequestMessage(method, address);
            foreach (string key in Headers.AllKeys)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                var value = Headers[key];
                if (string.IsNullOrWhiteSpace(value)) continue;
                request.Headers.TryAddWithoutValidation(key, value);
            }
            return request;
        }

        CancellationTokenSource CreateRequestCancellation(CancellationToken cancellationToken)
        {
            if (TimeoutMilliseconds > 0)
            {
                var linked = cancellationToken.CanBeCanceled
                    ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                    : new CancellationTokenSource();
                linked.CancelAfter(TimeoutMilliseconds);
                return linked;
            }

            return cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : null;
        }

        static CancellationToken ResolveRequestToken(CancellationTokenSource requestCancellation, CancellationToken fallbackToken)
        {
            return requestCancellation == null ? fallbackToken : requestCancellation.Token;
        }

        static async Task<byte[]> ReadStreamWithByteLimitAsync(Stream stream, long maxBytes, CancellationToken cancellationToken)
        {
            if (maxBytes <= 0)
            {
                using (var buffer = new MemoryStream())
                {
                    await stream.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
                    return buffer.ToArray();
                }
            }

            using (var buffer = new MemoryStream())
            {
                var chunk = new byte[81920];
                long total = 0;
                int read;
                while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > maxBytes)
                    {
                        throw new IOException("HTTP response exceeded maximum allowed size (" + maxBytes + " bytes).");
                    }
                    await buffer.WriteAsync(chunk, 0, read, cancellationToken).ConfigureAwait(false);
                }
                return buffer.ToArray();
            }
        }

        static async Task CopyStreamToFileWithLimitAsync(Stream source, Stream destination, long maxBytes, CancellationToken cancellationToken)
        {
            if (maxBytes <= 0)
            {
                await source.CopyToAsync(destination, 81920, cancellationToken).ConfigureAwait(false);
                return;
            }

            var chunk = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(chunk, 0, chunk.Length, cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    throw new IOException("HTTP download exceeded maximum allowed size (" + maxBytes + " bytes).");
                }
                await destination.WriteAsync(chunk, 0, read, cancellationToken).ConfigureAwait(false);
            }
        }

        static string DecodeResponseBody(byte[] bytes, Encoding fallbackEncoding, HttpResponseMessage response)
        {
            var charset = response.Content.Headers.ContentType == null ? null : response.Content.Headers.ContentType.CharSet;
            if (!string.IsNullOrWhiteSpace(charset))
            {
                try
                {
                    return System.Text.Encoding.GetEncoding(charset).GetString(bytes);
                }
                catch
                {
                }
            }

            return (fallbackEncoding ?? System.Text.Encoding.UTF8).GetString(bytes);
        }

        public async Task<string> DownloadStringAsync(string address, CancellationToken cancellationToken = default(CancellationToken))
        {
            using (var request = BuildRequest(HttpMethod.Get, address))
            using (var requestCancellation = CreateRequestCancellation(cancellationToken))
            {
                var token = ResolveRequestToken(requestCancellation, cancellationToken);
                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    using (var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
                    {
                        var bytes = await ReadStreamWithByteLimitAsync(stream, MaxStringResponseBytes, token).ConfigureAwait(false);
                        return DecodeResponseBody(bytes, Encoding, response);
                    }
                }
            }
        }

        public async Task DownloadFileAsync(string address, string filePath, CancellationToken cancellationToken = default(CancellationToken))
        {
            using (var request = BuildRequest(HttpMethod.Get, address))
            using (var requestCancellation = CreateRequestCancellation(cancellationToken))
            {
                var token = ResolveRequestToken(requestCancellation, cancellationToken);
                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    try
                    {
                        using (var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
                        using (var target = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                        {
                            await CopyStreamToFileWithLimitAsync(stream, target, MaxFileDownloadBytes, token).ConfigureAwait(false);
                            await target.FlushAsync(token).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                        {
                            try
                            {
                                File.Delete(filePath);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine("PixelVault TimeoutWebClient: could not delete partial download " + filePath + " — " + ex.Message);
                            }
                        }
                        throw;
                    }
                }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            client.Dispose();
            handler.Dispose();
        }
    }
}
