using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace PixelVaultNative
{
    // Synchronous wrapper kept for legacy call sites. Any heavy use of this type must stay off the WPF UI thread.
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

        public string DownloadString(string address, CancellationToken cancellationToken = default(CancellationToken))
        {
            using (var request = BuildRequest(HttpMethod.Get, address))
            using (var requestCancellation = CreateRequestCancellation(cancellationToken))
            using (var response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ResolveRequestToken(requestCancellation, cancellationToken)).GetAwaiter().GetResult())
            {
                response.EnsureSuccessStatusCode();
                using (var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                using (var buffer = new MemoryStream())
                {
                    stream.CopyToAsync(buffer, 81920, ResolveRequestToken(requestCancellation, cancellationToken)).GetAwaiter().GetResult();
                    var bytes = buffer.ToArray();
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

                    return (Encoding ?? System.Text.Encoding.UTF8).GetString(bytes);
                }
            }
        }

        public void DownloadFile(string address, string filePath, CancellationToken cancellationToken = default(CancellationToken))
        {
            using (var request = BuildRequest(HttpMethod.Get, address))
            using (var requestCancellation = CreateRequestCancellation(cancellationToken))
            using (var response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ResolveRequestToken(requestCancellation, cancellationToken)).GetAwaiter().GetResult())
            {
                response.EnsureSuccessStatusCode();
                try
                {
                    using (var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                    using (var target = File.Create(filePath))
                    {
                        stream.CopyToAsync(target, 81920, ResolveRequestToken(requestCancellation, cancellationToken)).GetAwaiter().GetResult();
                    }
                }
                catch
                {
                    if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                    {
                        try { File.Delete(filePath); } catch { }
                    }
                    throw;
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
