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
    // Sync entry points call the async implementations with GetAwaiter().GetResult(); prefer DownloadStringAsync/DownloadFileAsync with await off the UI thread so HTTP does not block a thread-pool thread.
    sealed class TimeoutWebClient : IDisposable
    {
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

        public string DownloadString(string address, CancellationToken cancellationToken = default(CancellationToken))
        {
            return DownloadStringAsync(address, cancellationToken).GetAwaiter().GetResult();
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
                    using (var buffer = new MemoryStream())
                    {
                        await stream.CopyToAsync(buffer, 81920, token).ConfigureAwait(false);
                        var bytes = buffer.ToArray();
                        return DecodeResponseBody(bytes, Encoding, response);
                    }
                }
            }
        }

        public void DownloadFile(string address, string filePath, CancellationToken cancellationToken = default(CancellationToken))
        {
            DownloadFileAsync(address, filePath, cancellationToken).GetAwaiter().GetResult();
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
                            await stream.CopyToAsync(target, 81920, token).ConfigureAwait(false);
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
