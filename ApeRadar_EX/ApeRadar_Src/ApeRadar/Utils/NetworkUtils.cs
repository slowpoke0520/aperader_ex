using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ApeRadar.Utils
{
    static internal class NetworkUtils
    {
        public const int MaxConcurrentHttpRequests = 10;
        private static long totalHttpGetCount;
        public static long TotalHttpGetCount => Interlocked.Read(ref totalHttpGetCount);

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
            if (!hc.DefaultRequestHeaders.Contains("X-Requested-With"))
            {
                hc.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
            }
            if (!hc.DefaultRequestHeaders.UserAgent.Any())
            {
                hc.DefaultRequestHeaders.UserAgent.ParseAdd("ApeRadar-Updater/1.0");
            }
        }

        public static async Task<string> HttpGet(string url)
        {
            try
            {
                await HttpSemaphore.WaitAsync();
                try
                {
                    Interlocked.Increment(ref totalHttpGetCount);
                    using HttpResponseMessage response = await hc.GetAsync(url);
                    response.EnsureSuccessStatusCode();
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
                    response.EnsureSuccessStatusCode();
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
                    response.EnsureSuccessStatusCode();
                    string temporaryFilename = $"{filename}.download";
                    try
                    {
                        using (FileStream fs = new(temporaryFilename, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await response.Content.CopyToAsync(fs);
                            await fs.FlushAsync();
                        }
                        File.Move(temporaryFilename, filename, true);
                        return filename;
                    }
                    finally
                    {
                        if (File.Exists(temporaryFilename))
                        {
                            File.Delete(temporaryFilename);
                        }
                    }
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
