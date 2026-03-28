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

        public string DownloadString(string address)
        {
            using (var request = BuildRequest(HttpMethod.Get, address))
            using (var cts = new CancellationTokenSource(TimeoutMilliseconds))
            using (var response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).GetAwaiter().GetResult())
            {
                response.EnsureSuccessStatusCode();
                var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
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

        public void DownloadFile(string address, string filePath)
        {
            using (var request = BuildRequest(HttpMethod.Get, address))
            using (var cts = new CancellationTokenSource(TimeoutMilliseconds))
            using (var response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).GetAwaiter().GetResult())
            {
                response.EnsureSuccessStatusCode();
                using (var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                using (var target = File.Create(filePath))
                {
                    stream.CopyTo(target);
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
