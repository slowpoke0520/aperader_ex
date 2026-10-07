using System;

namespace ApeRadar.Utils
{
    internal enum ApiFailureKind
    {
        None,
        Cancelled,
        Timeout,
        RateLimited,
        Network,
        Server,
        Unauthorized,
        NotFound,
        InvalidResponse,
        HiddenProfile,
        Unknown
    }

    internal readonly record struct ApiResult<T>(T? Value, ApiFailureKind Failure, string? Detail = null, TimeSpan? RetryAfter = null)
    {
        public bool IsSuccess => Failure == ApiFailureKind.None;

        public static ApiResult<T> Success(T value) => new(value, ApiFailureKind.None);
        public static ApiResult<T> Failed(ApiFailureKind failure, string? detail = null, TimeSpan? retryAfter = null) =>
            new(default, failure, detail, retryAfter);
    }
}
