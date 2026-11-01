using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace ApeRadar.Utils
{
    // HttpClient.DefaultProxy is a process-wide snapshot on .NET 8. Keep the handler
    // and its in-flight requests alive, but resolve each new request from current settings.
    internal sealed class RefreshableWebProxy : IWebProxy
    {
        private readonly Func<WindowsProxySettings> readWindowsSettings;
        private readonly IAutomaticProxyResolver automaticResolver;
        private readonly object gate = new();
        private readonly EnvironmentProxy? environmentProxy;
        private WindowsProxySettings? windowsSettings;
        private WindowsProxy? windowsProxy;
        private readonly ProxyCredentials credentials = new();
        private readonly Action<string>? trace;
        private bool settingsReadFailed;
        private static readonly Uri UnavailableProxy = new("proxy-settings-unavailable://unavailable");
        private static readonly AsyncLocal<RequestScope?> currentRequest = new();

        public RefreshableWebProxy() : this(ProxyEnvironment.Read, WindowsProxyNative.ReadSettings, new WindowsAutomaticProxyResolver(), LogUtils.WriteInfo) { }

        internal RefreshableWebProxy(Func<ProxyEnvironment> readEnvironment,
            Func<WindowsProxySettings> readWindowsSettings, IAutomaticProxyResolver automaticResolver, Action<string>? trace = null)
        {
            this.readWindowsSettings = readWindowsSettings;
            this.automaticResolver = automaticResolver;
            this.trace = trace;
            // Environment variables are process startup settings, as with .NET's default
            // proxy. Only Windows settings are refreshed; do not change authentication
            // on an existing pool when an environment variable is edited in-process.
            environmentProxy = EnvironmentProxy.Create(readEnvironment());
            environmentProxy?.RegisterCredentials(credentials);
            if (environmentProxy != null) Trace("HTTP proxy source=environment; using process environment configuration.");
        }

        public ICredentials? Credentials { get => credentials; set => credentials.Override = value; }

        internal RequestScope BeginRequest() => new();

        internal sealed class RequestScope : IDisposable
        {
            private readonly RequestScope? previous = currentRequest.Value;
            internal Action? MarkProxyFailed;
            public RequestScope() => currentRequest.Value = this;
            public void ReportFailure(Exception exception)
            {
                if (exception is not HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError }) return;
                for (Exception? cause = exception.InnerException; cause != null; cause = cause.InnerException)
                    if (cause is SocketException) { MarkProxyFailed?.Invoke(); return; }
            }
            public void Dispose() => currentRequest.Value = previous;
        }

        // SocketsHttpHandler accepts null from GetProxy as DIRECT. Resolve once there,
        // instead of racing two independently refreshed IsBypassed/GetProxy decisions.
        public bool IsBypassed(Uri host) => false;

        public Uri? GetProxy(Uri destination)
        {
            if (currentRequest.Value is RequestScope request) request.MarkProxyFailed = null;
            if (environmentProxy != null) return environmentProxy.GetProxy(destination);
            WindowsProxy? fromWindows = null;
            lock (gate)
            {
                try
                {
                    WindowsProxySettings currentWindows = readWindowsSettings();
                    if (currentWindows != windowsSettings)
                    {
                        windowsSettings = currentWindows;
                        windowsProxy = new WindowsProxy(currentWindows, automaticResolver);
                        string mode = currentWindows.HasAutomaticSettings ? "automatic" : string.IsNullOrWhiteSpace(currentWindows.Proxy) ? "direct" : "manual";
                        Trace($"HTTP proxy source=Windows; mode={mode}; new requests use refreshed configuration.");
                    }
                    if (settingsReadFailed) Trace("HTTP proxy settings read recovered.");
                    settingsReadFailed = false;
                }
                catch (Exception)
                {
                    // The handler swallows exceptions from IWebProxy and silently connects
                    // directly. Retain a known configuration instead; without one return an
                    // unsupported scheme, which the handler rejects outside that catch.
                    if (!settingsReadFailed) Trace(windowsProxy != null
                        ? "HTTP proxy settings read failed; retaining last known configuration."
                        : "HTTP proxy settings read failed; requests blocked until configuration is readable.");
                    settingsReadFailed = true;
                }
                fromWindows = windowsProxy;
            }
            // PAC evaluation can block; it must not block settings refresh or other requests.
            try
            {
                return fromWindows != null ? fromWindows.GetProxy(destination) : UnavailableProxy;
            }
            catch (Exception) { return UnavailableProxy; }
        }

        private void Trace(string message)
        {
            try { trace?.Invoke(message); }
            catch (Exception) { /* Diagnostics must never make a proxy failure fall back to DIRECT. */ }
        }

        private sealed class ProxyCredentials : ICredentials
        {
            public ICredentials? Override;
            public readonly ConcurrentDictionary<string, NetworkCredential> KnownCredentials = new(StringComparer.Ordinal);
            public NetworkCredential? GetCredential(Uri uri, string authType) =>
                Volatile.Read(ref Override)?.GetCredential(uri, authType) ??
                (KnownCredentials.TryGetValue(uri.AbsoluteUri, out NetworkCredential? value) ? value : null);
        }

        private sealed class EnvironmentProxy
        {
            private readonly Uri? http;
            private readonly Uri? https;
            private readonly string[] bypass;

            private EnvironmentProxy(Uri? http, Uri? https, string? noProxy)
            {
                this.http = http;
                this.https = https;
                bypass = noProxy?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            }

            public static EnvironmentProxy? Create(ProxyEnvironment settings)
            {
                Uri? all = ParseEnvironmentUri(settings.All);
                Uri? http = (settings.Cgi == null ? ParseEnvironmentUri(settings.Http) : null) ?? all;
                Uri? https = ParseEnvironmentUri(settings.Https) ?? all;
                return http == null && https == null ? null : new EnvironmentProxy(http, https, settings.NoProxy);
            }

            public Uri? GetProxy(Uri destination)
            {
                foreach (string entry in bypass)
                {
                    if (entry[0] == '.'
                        ? destination.Host.Equals(entry[1..], StringComparison.OrdinalIgnoreCase) || destination.Host.EndsWith(entry, StringComparison.OrdinalIgnoreCase)
                        : destination.Host.Equals(entry, StringComparison.OrdinalIgnoreCase)) return null;
                }
                return destination.Scheme is "http" or "ws" ? http : https;
            }

            public NetworkCredential? GetCredential(Uri proxy)
            {
                Uri? configured = proxy.AbsoluteUri == http?.AbsoluteUri ? http : proxy.AbsoluteUri == https?.AbsoluteUri ? https : null;
                if (configured == null || string.IsNullOrEmpty(configured.UserInfo)) return null;
                if (configured.UserInfo == ":") return CredentialCache.DefaultNetworkCredentials;
                string userInfo = Uri.UnescapeDataString(configured.UserInfo);
                int separator = userInfo.IndexOf(':');
                string user = separator < 0 ? userInfo : userInfo[..separator];
                string password = separator < 0 ? "" : userInfo[(separator + 1)..];
                int domain = user.IndexOf('\\');
                return domain < 0 ? new NetworkCredential(user, password) : new NetworkCredential(user[(domain + 1)..], password, user[..domain]);
            }

            public void RegisterCredentials(ProxyCredentials target)
            {
                foreach (Uri? uri in new[] { http, https })
                    if (uri != null && GetCredential(uri) is NetworkCredential value) target.KnownCredentials[uri.AbsoluteUri] = value;
            }

            // Match .NET's supported environment schemes, scheme-less host:port,
            // protocol-specific precedence, credentials and literal NO_PROXY domains.
            private static Uri? ParseEnvironmentUri(string? value)
            {
                if (string.IsNullOrEmpty(value)) return null;
                string scheme = "http";
                foreach (string supported in new[] { "http", "https", "socks4", "socks4a", "socks5" })
                {
                    if (!value.StartsWith(supported + "://", StringComparison.OrdinalIgnoreCase)) continue;
                    scheme = supported;
                    value = value[(supported.Length + 3)..];
                    break;
                }
                string? user = null;
                string? password = null;
                int at = value.LastIndexOf('@');
                if (at >= 0)
                {
                    string authentication = Uri.UnescapeDataString(value[..at]);
                    value = value[(at + 1)..];
                    int colon = authentication.IndexOf(':');
                    user = colon < 0 ? authentication : authentication[..colon];
                    password = colon < 0 ? null : authentication[(colon + 1)..];
                }
                int port = scheme == "https" ? 443 : 80;
                int portSeparator = value.LastIndexOf(':');
                if (portSeparator >= 0 && portSeparator > value.LastIndexOf(']'))
                {
                    string portText = new(value[(portSeparator + 1)..].TakeWhile(char.IsDigit).ToArray());
                    if (!ushort.TryParse(portText, out ushort parsed)) return null;
                    port = parsed;
                    value = value[..portSeparator];
                }
                try
                {
                    UriBuilder builder = new(scheme, value, port);
                    if (user != null) builder.UserName = Uri.EscapeDataString(user);
                    if (password != null) builder.Password = Uri.EscapeDataString(password);
                    Uri result = builder.Uri;
                    return user == "" && password == "" ? new Uri($"{scheme}://:@{result.Authority}") : result;
                }
                catch (UriFormatException) { return null; }
                catch (ArgumentException) { return null; }
            }
        }

        private sealed class WindowsProxy
        {
            private readonly WindowsProxySettings settings;
            private readonly IAutomaticProxyResolver automaticResolver;
            private long autoDetectionFailedUntil;
            private readonly ConcurrentDictionary<Uri, long> failedProxies = new();

            public WindowsProxy(WindowsProxySettings settings, IAutomaticProxyResolver automaticResolver)
            {
                this.settings = settings;
                this.automaticResolver = automaticResolver;
            }

            public Uri? GetProxy(Uri destination)
            {
                if (settings.HasAutomaticSettings && Environment.TickCount64 >= Interlocked.Read(ref autoDetectionFailedUntil))
                {
                    AutomaticProxyResult result = automaticResolver.Resolve(settings, destination);
                    if (result.Success) return SelectProxy(result.Proxy, result.Bypass, destination);
                    if (result.AutoDetectionFailed) Interlocked.Exchange(ref autoDetectionFailedUntil, Environment.TickCount64 + 120_000);
                }
                return SelectProxy(settings.Proxy, settings.Bypass, destination);
            }

            private Uri? SelectProxy(string? proxyList, string? bypassList, Uri destination)
            {
                Uri[] candidates = GetManualProxies(proxyList, bypassList, destination).ToArray();
                if (candidates.Length == 0) return null;
                long now = Environment.TickCount64;
                Uri chosen = candidates.FirstOrDefault(candidate => !failedProxies.TryGetValue(candidate, out long until) || until <= now)
                    ?? candidates.OrderBy(candidate => failedProxies[candidate]).First();
                if (currentRequest.Value is RequestScope request)
                    request.MarkProxyFailed = () => failedProxies[chosen] = Environment.TickCount64 + 30_000;
                return chosen;
            }
        }

        internal static Uri? SelectManualProxy(string? proxyList, string? bypassList, Uri destination)
            => GetManualProxies(proxyList, bypassList, destination).FirstOrDefault();

        private static IEnumerable<Uri> GetManualProxies(string? proxyList, string? bypassList, Uri destination)
        {
            if (string.IsNullOrWhiteSpace(proxyList) || IsWindowsBypassed(bypassList, destination)) yield break;
            bool secure = destination.Scheme is "https" or "wss";
            foreach (string part in proxyList.Split(new[] { ';', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = part;
                bool? forSecure = null;
                if (candidate.StartsWith("http=", StringComparison.Ordinal)) { forSecure = false; candidate = candidate[5..]; }
                else if (candidate.StartsWith("https=", StringComparison.Ordinal)) { forSecure = true; candidate = candidate[6..]; }
                if (candidate.StartsWith("http://", StringComparison.Ordinal)) { forSecure = false; candidate = candidate[7..]; }
                else if (candidate.StartsWith("https://", StringComparison.Ordinal)) { forSecure = true; candidate = candidate[8..]; }
                if (forSecure.HasValue && forSecure.Value != secure) continue;
                if (Uri.TryCreate("http://" + candidate, UriKind.Absolute, out Uri? parsed)) yield return parsed;
            }
        }

        private static bool IsWindowsBypassed(string? bypassList, Uri destination)
        {
            foreach (string raw in (bypassList ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (raw.Equals("<local>", StringComparison.OrdinalIgnoreCase))
                {
                    if (destination.IsLoopback || (destination.HostNameType != UriHostNameType.IPv6 && !destination.IdnHost.Contains('.'))) return true;
                    if (IPAddress.TryParse(destination.IdnHost, out IPAddress? address) && NetworkInterface.GetAllNetworkInterfaces()
                        .Any(network => network.GetIPProperties().UnicastAddresses.Any(local => local.Address.Equals(address)))) return true;
                    continue;
                }
                string pattern = raw;
                if (pattern.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) pattern = pattern[7..];
                else if (pattern.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) pattern = pattern[8..];
                pattern = pattern.Trim('[', ']');
                if (WildcardMatches(destination.IdnHost, pattern)) return true;
            }
            return false;
        }

        private static bool WildcardMatches(string host, string pattern)
        {
            int h = 0, p = 0, star = -1, retry = 0;
            while (h < host.Length)
            {
                if (p < pattern.Length && char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(host[h])) { p++; h++; }
                else if (p < pattern.Length && pattern[p] == '*') { star = p++; retry = h; }
                else if (star >= 0) { p = star + 1; h = ++retry; }
                else return false;
            }
            while (p < pattern.Length && pattern[p] == '*') p++;
            return p == pattern.Length;
        }
    }

    internal sealed record ProxyEnvironment(string? Http = null, string? Https = null, string? All = null, string? NoProxy = null, string? Cgi = null)
    {
        public static ProxyEnvironment Read() => new(Environment.GetEnvironmentVariable("HTTP_PROXY"), Environment.GetEnvironmentVariable("HTTPS_PROXY"),
            Environment.GetEnvironmentVariable("ALL_PROXY"), Environment.GetEnvironmentVariable("NO_PROXY"), Environment.GetEnvironmentVariable("GATEWAY_INTERFACE"));
    }

    internal sealed record WindowsProxySettings(string? Proxy = null, string? Bypass = null, string? AutoConfigUrl = null, bool AutoDetect = false)
    {
        public bool HasAutomaticSettings => AutoDetect || !string.IsNullOrEmpty(AutoConfigUrl);
    }

    internal readonly record struct AutomaticProxyResult(bool Success, string? Proxy = null, string? Bypass = null, bool AutoDetectionFailed = false, int ErrorCode = 0);
    internal interface IAutomaticProxyResolver { AutomaticProxyResult Resolve(WindowsProxySettings settings, Uri destination); }

    internal sealed class WindowsAutomaticProxyResolver : IAutomaticProxyResolver, IDisposable
    {
        private readonly Lazy<WindowsProxyNative.SafeWinHttpHandle> session = new(WindowsProxyNative.OpenSession);
        public AutomaticProxyResult Resolve(WindowsProxySettings settings, Uri destination) => WindowsProxyNative.ResolveAutomatic(session.Value, settings, destination);
        public void Dispose() { if (session.IsValueCreated) session.Value.Dispose(); }
    }

    internal static class WindowsProxyNative
    {
        public static WindowsProxySettings ReadSettings()
        {
            CurrentUserProxyConfig config = default;
            try
            {
                if (!WinHttpGetIEProxyConfigForCurrentUser(out config)) throw new Win32Exception(Marshal.GetLastWin32Error());
                return new(Marshal.PtrToStringUni(config.Proxy), Marshal.PtrToStringUni(config.Bypass), Marshal.PtrToStringUni(config.AutoConfigUrl), config.AutoDetect);
            }
            finally
            {
                Marshal.FreeHGlobal(config.Proxy);
                Marshal.FreeHGlobal(config.Bypass);
                Marshal.FreeHGlobal(config.AutoConfigUrl);
            }
        }

        internal static SafeWinHttpHandle OpenSession() => WinHttpOpen(null, 1, null, null, 0);

        internal static AutomaticProxyResult ResolveAutomatic(SafeWinHttpHandle session, WindowsProxySettings settings, Uri destination)
        {
            if (session.IsInvalid) return new(false, ErrorCode: Marshal.GetLastWin32Error());
            AutoProxyOptions options = new()
            {
                Flags = (settings.AutoDetect ? 1u : 0u) | (!string.IsNullOrEmpty(settings.AutoConfigUrl) ? 2u : 0u),
                AutoDetectFlags = settings.AutoDetect ? 3u : 0u,
                AutoConfigUrl = settings.AutoConfigUrl
            };
            string url = destination.Scheme is "ws" or "wss"
                ? new UriBuilder(destination) { Scheme = destination.Scheme == "wss" ? "https" : "http" }.Uri.AbsoluteUri
                : destination.AbsoluteUri;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                ProxyInfo info = default;
                try
                {
                    if (WinHttpGetProxyForUrl(session, url, ref options, out info))
                        return new(true, Marshal.PtrToStringUni(info.Proxy), Marshal.PtrToStringUni(info.Bypass));
                    int error = Marshal.GetLastWin32Error();
                    if (error != 12015 || attempt != 0) return new(false, AutoDetectionFailed: error == 12180, ErrorCode: error);
                    options.AutoLogonIfChallenged = true;
                }
                finally
                {
                    Marshal.FreeHGlobal(info.Proxy);
                    Marshal.FreeHGlobal(info.Bypass);
                }
            }
            return new(false);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CurrentUserProxyConfig
        {
            [MarshalAs(UnmanagedType.Bool)] public bool AutoDetect;
            public IntPtr AutoConfigUrl;
            public IntPtr Proxy;
            public IntPtr Bypass;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct AutoProxyOptions
        {
            public uint Flags;
            public uint AutoDetectFlags;
            [MarshalAs(UnmanagedType.LPWStr)] public string? AutoConfigUrl;
            public IntPtr Reserved1;
            public uint Reserved2;
            [MarshalAs(UnmanagedType.Bool)] public bool AutoLogonIfChallenged;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProxyInfo { public uint AccessType; public IntPtr Proxy; public IntPtr Bypass; }

        internal sealed class SafeWinHttpHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            private SafeWinHttpHandle() : base(true) { }
            protected override bool ReleaseHandle() => WinHttpCloseHandle(handle);
        }

        [DllImport("winhttp.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WinHttpGetIEProxyConfigForCurrentUser(out CurrentUserProxyConfig config);
        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeWinHttpHandle WinHttpOpen(string? userAgent, uint accessType, string? proxy, string? bypass, uint flags);
        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WinHttpGetProxyForUrl(SafeWinHttpHandle session, string url, ref AutoProxyOptions options, out ProxyInfo info);
        [DllImport("winhttp.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WinHttpCloseHandle(IntPtr handle);
    }
}
