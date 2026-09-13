using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ApeRadar.Utils
{
    internal sealed class NetworkRequestException : HttpRequestException
    {
        public ApiFailureKind FailureKind { get; }
        public TimeSpan? RetryAfter { get; }

        public NetworkRequestException(ApiFailureKind failureKind, Exception? inner = null, HttpStatusCode? statusCode = null, TimeSpan? retryAfter = null)
            : base("HttpRequestFailed", inner, statusCode)
        {
            FailureKind = failureKind;
            RetryAfter = retryAfter;
        }
    }

    static internal class NetworkUtils
    {
        public const int MaxConcurrentHttpRequests = 10;
        public const int MaxConcurrentRequestsPerOrigin = 6;
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
        private static long totalHttpGetCount;
        private static long totalHttpRequestCount;
        private static long totalRetryCount;

        public static long TotalHttpGetCount => Interlocked.Read(ref totalHttpGetCount);
        public static long TotalHttpRequestCount => Interlocked.Read(ref totalHttpRequestCount);
        public static long TotalRetryCount => Interlocked.Read(ref totalRetryCount);

        public static (long Gets, long Requests, long Retries) GetMetricsSnapshot() =>
            (TotalHttpGetCount, TotalHttpRequestCount, TotalRetryCount);

        static readonly SemaphoreSlim HttpSemaphore = new(MaxConcurrentHttpRequests);
        static readonly ConcurrentDictionary<string, SemaphoreSlim> OriginSemaphores = new(StringComparer.OrdinalIgnoreCase);
        static readonly ConcurrentDictionary<string, SharedTextRequest> InflightGetRequests = new(StringComparer.Ordinal);

        static readonly HttpClientHandler handler = new()
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            MaxConnectionsPerServer = MaxConcurrentRequestsPerOrigin,
            UseCookies = false,
        };

        static readonly HttpClient hc = new(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        public static void InitializeHttpClient()
        {
            if (!hc.DefaultRequestHeaders.Contains("X-Requested-With"))
            {
                hc.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
            }
            if (!hc.DefaultRequestHeaders.UserAgent.Any())
            {
                hc.DefaultRequestHeaders.UserAgent.ParseAdd($"ApeRadar-EX/{Properties.Settings.Default.SoftwareVersion}");
            }
        }

        public static async Task<string> HttpGet(string url, CancellationToken cancellationToken = default)
        {
            SharedTextRequest request = InflightGetRequests.GetOrAdd(url,
                key => new SharedTextRequest(token => SendTextAsync(HttpMethod.Get, key, null, null, token)));
            try
            {
                return await request.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (request.IsCompleted || request.WaiterCount == 0)
                {
                    InflightGetRequests.TryRemove(new KeyValuePair<string, SharedTextRequest>(url, request));
                }
            }
        }

        public static Task<string> HttpPost(string url, string content, string mediaType, CancellationToken cancellationToken = default) =>
            SendTextAsync(HttpMethod.Post, url, content, mediaType, cancellationToken);

        private static async Task<string> SendTextAsync(HttpMethod method, string url, string? content, string? mediaType, CancellationToken cancellationToken)
        {
            Uri uri = ValidateUri(url);
            return await ExecuteWithRetryAsync(uri, async token =>
            {
                using HttpRequestMessage request = new(method, uri);
                if (content != null)
                {
                    request.Content = new StringContent(content, Encoding.UTF8, mediaType ?? "application/json");
                }
                using HttpResponseMessage response = await hc.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                await EnsureSuccessAsync(response).ConfigureAwait(false);
                return await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            }, method == HttpMethod.Get, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<string> HttpDownloadFile(string url, string filename, CancellationToken cancellationToken = default)
        {
            Uri uri = ValidateUri(url);
            return await ExecuteWithRetryAsync(uri, async token =>
            {
                using HttpRequestMessage request = new(HttpMethod.Get, uri);
                using HttpResponseMessage response = await hc.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                await EnsureSuccessAsync(response).ConfigureAwait(false);
                string temporaryFilename = $"{filename}.download";
                try
                {
                    await using FileStream fs = new(temporaryFilename, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                    await response.Content.CopyToAsync(fs, token).ConfigureAwait(false);
                    await fs.FlushAsync(token).ConfigureAwait(false);
                    File.Move(temporaryFilename, filename, true);
                    return filename;
                }
                finally
                {
                    if (File.Exists(temporaryFilename)) File.Delete(temporaryFilename);
                }
            }, true, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<T> ExecuteWithRetryAsync<T>(Uri uri, Func<CancellationToken, Task<T>> operation, bool isGet, CancellationToken cancellationToken)
        {
            const int maximumAttempts = 3;
            for (int attempt = 1; attempt <= maximumAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await ExecuteThrottledAsync(uri, operation, isGet, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (attempt < maximumAttempts && IsTransient(ex, out TimeSpan? serverDelay))
                {
                    Interlocked.Increment(ref totalRetryCount);
                    TimeSpan delay = serverDelay ?? TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1) + Random.Shared.Next(50, 180));
                    LogUtils.WriteInfo($"HTTP transient failure; retry {attempt}/{maximumAttempts - 1} after {delay.TotalMilliseconds:0} ms: {uri.Host}");
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (NetworkRequestException)
                {
                    throw;
                }
                catch (OperationCanceledException ex)
                {
                    throw new NetworkRequestException(ApiFailureKind.Timeout, ex);
                }
                catch (HttpRequestException ex)
                {
                    throw new NetworkRequestException(ApiFailureKind.Network, ex, ex.StatusCode);
                }
                catch (Exception ex)
                {
                    throw new NetworkRequestException(ApiFailureKind.Unknown, ex);
                }
            }
            throw new NetworkRequestException(ApiFailureKind.Unknown);
        }

        private static async Task<T> ExecuteThrottledAsync<T>(Uri uri, Func<CancellationToken, Task<T>> operation, bool isGet, CancellationToken cancellationToken)
        {
            SemaphoreSlim originSemaphore = OriginSemaphores.GetOrAdd(uri.GetLeftPart(UriPartial.Authority), _ => new SemaphoreSlim(MaxConcurrentRequestsPerOrigin));
            await HttpSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await originSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    Interlocked.Increment(ref totalHttpRequestCount);
                    if (isGet) Interlocked.Increment(ref totalHttpGetCount);
                    using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(RequestTimeout);
                    return await operation(timeout.Token).ConfigureAwait(false);
                }
                finally
                {
                    originSemaphore.Release();
                }
            }
            finally
            {
                HttpSemaphore.Release();
            }
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return;
            TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
            if (!retryAfter.HasValue && response.Headers.RetryAfter?.Date is DateTimeOffset retryAt)
            {
                retryAfter = retryAt - DateTimeOffset.UtcNow;
                if (retryAfter < TimeSpan.Zero) retryAfter = TimeSpan.Zero;
            }
            ApiFailureKind kind = response.StatusCode switch
            {
                HttpStatusCode.TooManyRequests => ApiFailureKind.RateLimited,
                HttpStatusCode.RequestTimeout => ApiFailureKind.Timeout,
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ApiFailureKind.Unauthorized,
                HttpStatusCode.NotFound => ApiFailureKind.NotFound,
                >= HttpStatusCode.InternalServerError => ApiFailureKind.Server,
                _ => ApiFailureKind.InvalidResponse
            };
            string detail = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new NetworkRequestException(kind, new HttpRequestException(detail), response.StatusCode, retryAfter);
        }

        private static bool IsTransient(Exception exception, out TimeSpan? retryAfter)
        {
            retryAfter = null;
            if (exception is NetworkRequestException network)
            {
                retryAfter = network.RetryAfter;
                return network.FailureKind is ApiFailureKind.RateLimited or ApiFailureKind.Timeout or ApiFailureKind.Network or ApiFailureKind.Server;
            }
            return exception is HttpRequestException or OperationCanceledException;
        }

        private static Uri ValidateUri(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new NetworkRequestException(ApiFailureKind.InvalidResponse, new UriFormatException(url));
            }
            return uri;
        }

        private sealed class SharedTextRequest
        {
            private readonly CancellationTokenSource requestCancellation = new();
            private readonly Lazy<Task<string>> task;
            private int waiterCount;

            public SharedTextRequest(Func<CancellationToken, Task<string>> factory)
            {
                task = new Lazy<Task<string>>(() => factory(requestCancellation.Token), LazyThreadSafetyMode.ExecutionAndPublication);
            }

            public int WaiterCount => Volatile.Read(ref waiterCount);
            public bool IsCompleted => task.IsValueCreated && task.Value.IsCompleted;

            public async Task<string> WaitAsync(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref waiterCount);
                Task<string> sharedTask = task.Value;
                try
                {
                    return await sharedTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    if (Interlocked.Decrement(ref waiterCount) == 0 && !sharedTask.IsCompleted)
                    {
                        requestCancellation.Cancel();
                    }
                }
            }
        }
    }
}
