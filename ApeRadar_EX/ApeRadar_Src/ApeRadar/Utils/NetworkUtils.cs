using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ApeRadar.Utils
{
    static internal class NetworkUtils
    {
        public const int MaxConcurrentHttpRequests = 10;
        public static long TotalHttpGetCount = 0;

        static readonly SemaphoreSlim HttpSemaphore = new(MaxConcurrentHttpRequests);

        static readonly HttpClientHandler handler = new()
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
            MaxConnectionsPerServer = 16,
            UseCookies = false,
        };

        static readonly HttpClient hc = new(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(20000)
        };

        public static void InitializeHttpClient()
        {
            hc.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        }

        public static async Task<string> HttpGet(string url)
        {
            try
            {
                await HttpSemaphore.WaitAsync();
                try
                {
                    TotalHttpGetCount++;
                    using HttpResponseMessage response = await hc.GetAsync(url);
                    return await response.Content.ReadAsStringAsync();
                }
                finally
                {
                    HttpSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                throw new HttpRequestException("HttpRequestFailed", ex);
            }
        }

        public static async Task<string> HttpPost(string url, string content, string mediaType)
        {
            try
            {
                await HttpSemaphore.WaitAsync();
                try
                {
                    using HttpResponseMessage response = await hc.PostAsync(url, new StringContent(content, Encoding.UTF8, mediaType));
                    return await response.Content.ReadAsStringAsync();
                }
                finally
                {
                    HttpSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                throw new HttpRequestException("HttpRequestFailed", ex);
            }
        }

        public static async Task<string> HttpDownloadFile(string url, string filename)
        {
            try
            {
                await HttpSemaphore.WaitAsync();
                try
                {
                    using HttpResponseMessage response = await hc.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    using FileStream fs = new(filename, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                    await response.Content.CopyToAsync(fs);
                    return filename;
                }
                finally
                {
                    HttpSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                throw new HttpRequestException("HttpRequestFailed", ex);
            }
        }
    }
}
