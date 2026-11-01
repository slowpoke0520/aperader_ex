using ApeRadar.Utils;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Xunit;

namespace ApeRadar.Tests;

public sealed class RefreshableWebProxyTests
{
    [Fact]
    public async Task OneClient_ChangesProxyPort_DisablesAndReenablesProxy()
    {
        await using LoopbackServer origin = new("direct");
        await using LoopbackServer first = new("first");
        await using LoopbackServer second = new("second");
        WindowsProxySettings settings = new();
        RefreshableWebProxy proxy = Create(() => settings);
        using HttpClient client = Client(proxy);

        Assert.Equal("direct", await client.GetStringAsync(origin.Address));
        settings = new(first.Address.Authority);
        Assert.Equal("first", await client.GetStringAsync(origin.Address));
        settings = new(second.Address.Authority);
        Assert.Equal("second", await client.GetStringAsync(origin.Address));
        settings = new();
        Assert.Equal("direct", await client.GetStringAsync(origin.Address));
        settings = new(first.Address.Authority);
        Assert.Equal("first", await client.GetStringAsync(origin.Address));
        Assert.Equal(2, origin.Requests.Count);
        Assert.Equal(2, first.Requests.Count);
        Assert.Single(second.Requests);
    }

    [Fact]
    public async Task ProxyChange_DoesNotCancelRequestAlreadyUsingPreviousProxy()
    {
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using LoopbackServer first = new("first", beforeResponse: () => release.Task);
        await using LoopbackServer second = new("second");
        WindowsProxySettings settings = new(first.Address.Authority);
        RefreshableWebProxy proxy = Create(() => Volatile.Read(ref settings));
        using HttpClient client = Client(proxy);
        Uri destination = new("http://unresolved.example.test/battle");

        Task<string> pending = client.GetStringAsync(destination);
        await first.FirstRequest.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Volatile.Write(ref settings, new(second.Address.Authority));
        Assert.Equal("second", await client.GetStringAsync(destination));
        Assert.False(pending.IsCompleted);
        release.SetResult();
        Assert.Equal("first", await pending);
    }

    [Fact]
    public async Task HttpsConnect_ChangesProxyPortWithoutRecreatingClient()
    {
        using RSA key = RSA.Create(2048);
        CertificateRequest request = new("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        // Windows Schannel requires an imported key rather than the ephemeral signing key.
        using X509Certificate2 certificate = new(generated.Export(X509ContentType.Pfx));
        await using LoopbackServer origin = new("encrypted", certificate: certificate);
        await using LoopbackServer first = new("unused");
        await using LoopbackServer second = new("unused");
        WindowsProxySettings settings = new(first.Address.Authority);
        using HttpClient client = Client(Create(() => settings), allowTestCertificate: true);

        Assert.Equal("encrypted", await client.GetStringAsync(origin.Address));
        settings = new(second.Address.Authority);
        Assert.Equal("encrypted", await client.GetStringAsync(origin.Address));
        settings = new();
        Assert.Equal("encrypted", await client.GetStringAsync(origin.Address));
        Assert.StartsWith("CONNECT ", Assert.Single(first.Requests));
        Assert.StartsWith("CONNECT ", Assert.Single(second.Requests));
        Assert.Equal(3, origin.Requests.Count);
    }

    [Fact]
    public async Task ConfigurationReadFailure_RetainsProxyAndRecoversOnNextRequest()
    {
        await using LoopbackServer origin = new("direct");
        await using LoopbackServer first = new("first");
        bool fail = false;
        WindowsProxySettings settings = new(first.Address.Authority);
        RefreshableWebProxy proxy = Create(() => fail ? throw new Win32Exception(5) : settings);
        using HttpClient client = Client(proxy);
        Assert.Equal("first", await client.GetStringAsync(origin.Address));
        fail = true;
        Assert.Equal("first", await client.GetStringAsync(origin.Address));
        Assert.Empty(origin.Requests);
        fail = false;
        settings = new();
        Assert.Equal("direct", await client.GetStringAsync(origin.Address));
    }

    [Fact]
    public async Task InitialConfigurationReadFailure_NeverSendsDirectRequest()
    {
        await using LoopbackServer origin = new("must-not-connect");
        RefreshableWebProxy proxy = new(() => new(), () => throw new Win32Exception(5), new FakeAutomatic(), _ => throw new IOException("Log unavailable"));
        using HttpClient client = Client(proxy);
        await Assert.ThrowsAsync<NotSupportedException>(() => client.GetStringAsync(origin.Address));
        Assert.Empty(origin.Requests);
    }

    [Fact]
    public void EnvironmentOverridesSystem_WithProtocolSpecificAndNoProxySemantics()
    {
        ProxyEnvironment environment = new(Http: "127.0.0.1:8101", All: "socks5://127.0.0.1:8102", NoProxy: ".example.test,exact.test");
        RefreshableWebProxy proxy = new(() => environment, () => throw new InvalidOperationException("Environment must take priority"), new FakeAutomatic());
        Assert.Equal(new Uri("http://127.0.0.1:8101"), proxy.GetProxy(new("http://elsewhere.test")));
        Assert.Equal(new Uri("socks5://127.0.0.1:8102"), proxy.GetProxy(new("https://elsewhere.test")));
        Assert.Null(proxy.GetProxy(new("https://example.test")));
        Assert.Null(proxy.GetProxy(new("https://a.example.test")));
        Assert.Null(proxy.GetProxy(new("https://exact.test")));
        Assert.NotNull(proxy.GetProxy(new("https://a.exact.test")));
        proxy = new(() => new(Http: "127.0.0.1:8101"), () => throw new InvalidOperationException(), new FakeAutomatic());
        Assert.Null(proxy.GetProxy(new("https://elsewhere.test")));
    }

    [Fact]
    public void NoProxyAlone_DoesNotSuppressSystemSettings_AndCgiIgnoresHttpProxy()
    {
        ProxyEnvironment environment = new(NoProxy: ".example.test");
        RefreshableWebProxy proxy = new(() => environment, () => new("127.0.0.1:8103"), new FakeAutomatic());
        Assert.Equal(new Uri("http://127.0.0.1:8103"), proxy.GetProxy(new("https://example.test")));
        proxy = new(() => new(Http: "127.0.0.1:8101", All: "127.0.0.1:8102", Cgi: "CGI/1.1"), () => new(), new FakeAutomatic());
        Assert.Equal(new Uri("http://127.0.0.1:8102"), proxy.GetProxy(new("http://example.test")));
    }

    [Fact]
    public void EnvironmentCredentials_AreCapturedAtStartupAndDoNotLeakToUncredentialedUri()
    {
        ProxyEnvironment environment = new(Https: "http://DOMAIN%5Cfirst:secret%3Aone@localhost:8101");
        RefreshableWebProxy proxy = new(() => environment, () => new(), new FakeAutomatic());
        ICredentials credentials = proxy.Credentials!;
        Uri first = proxy.GetProxy(new("https://example.test"))!;
        environment = new(Https: "http://second:secret2@localhost:8102");
        Uri second = proxy.GetProxy(new("https://example.test"))!;
        Assert.Equal(first.AbsoluteUri, second.AbsoluteUri);
        Assert.Same(credentials, proxy.Credentials);
        Assert.Equal("DOMAIN", credentials.GetCredential(first, "Basic")!.Domain);
        Assert.Equal("first", credentials.GetCredential(first, "Basic")!.UserName);
        Assert.Equal("secret:one", credentials.GetCredential(first, "Basic")!.Password);
        Assert.Null(credentials.GetCredential(new Uri("http://localhost:8101"), "Basic"));
    }

    [Fact]
    public void RefreshDiagnostics_OnlyLogChangesAndReadFailureTransitions()
    {
        WindowsProxySettings settings = new("localhost:8101");
        bool fail = false;
        List<string> messages = new();
        RefreshableWebProxy proxy = new(() => new(), () => fail ? throw new Win32Exception(5) : settings, new FakeAutomatic(), messages.Add);
        Uri destination = new("https://example.test");
        proxy.GetProxy(destination);
        proxy.GetProxy(destination);
        Assert.Single(messages);
        settings = new("localhost:8102");
        proxy.GetProxy(destination);
        fail = true;
        proxy.GetProxy(destination);
        proxy.GetProxy(destination);
        Assert.Equal(3, messages.Count);
        fail = false;
        proxy.GetProxy(destination);
        Assert.Equal(4, messages.Count);
        Assert.All(messages, message => Assert.DoesNotContain("810", message));
    }

    [Theory]
    [InlineData("https://api.example.test", "*.example.test", true)]
    [InlineData("https://example.test", "*.example.test", false)]
    [InlineData("https://EXACT.TEST", "exact.test", true)]
    [InlineData("https://printer", "<local>", true)]
    [InlineData("https://127.0.0.1", "<local>", true)]
    [InlineData("https://[::1]", "[::1]", true)]
    [InlineData("https://external.test", "<local>", false)]
    public void ManualBypassRules_ArePreserved(string destination, string bypass, bool expectedDirect)
    {
        Assert.Equal(expectedDirect, RefreshableWebProxy.SelectManualProxy("localhost:8101", bypass, new(destination)) == null);
    }

    [Fact]
    public void ManualProtocolSelection_IsPreserved()
    {
        const string configured = "http=localhost:8101;https=localhost:8102";
        Assert.Equal(new Uri("http://localhost:8101"), RefreshableWebProxy.SelectManualProxy(configured, null, new("http://example.test")));
        Assert.Equal(new Uri("http://localhost:8102"), RefreshableWebProxy.SelectManualProxy(configured, null, new("https://example.test")));
    }

    [Fact]
    public void Pac_DirectProxyManualFallbackAndConfigurationRefresh()
    {
        WindowsProxySettings settings = new("localhost:8101", AutoConfigUrl: "http://config.test/a.pac");
        FakeAutomatic resolver = new() { Result = new(true, "localhost:8102") };
        RefreshableWebProxy proxy = new(() => new(), () => settings, resolver);
        Uri destination = new("https://example.test");
        Assert.Equal(new Uri("http://localhost:8102"), proxy.GetProxy(destination));
        resolver.Result = new(true); // A PAC DIRECT result must override the manual proxy.
        Assert.Null(proxy.GetProxy(destination));
        resolver.Result = new(false);
        Assert.Equal(new Uri("http://localhost:8101"), proxy.GetProxy(destination));
        settings = new("localhost:8103");
        Assert.Equal(new Uri("http://localhost:8103"), proxy.GetProxy(destination));
        Assert.Equal(3, resolver.Calls);
    }

    [Fact]
    public async Task PacProxyList_UsesNextCandidateAfterConnectionRefusal()
    {
        await using LoopbackServer available = new("fallback");
        using TcpListener closedPort = new(IPAddress.Loopback, 0);
        closedPort.Start();
        int unavailablePort = ((IPEndPoint)closedPort.LocalEndpoint).Port;
        closedPort.Stop();
        FakeAutomatic resolver = new() { Result = new(true, $"127.0.0.1:{unavailablePort};{available.Address.Authority}") };
        RefreshableWebProxy proxy = new(() => new(), () => new(AutoConfigUrl: "http://config.test/a.pac"), resolver);
        using HttpClient client = Client(proxy);
        using (RefreshableWebProxy.RequestScope route = proxy.BeginRequest())
        {
            HttpRequestException failure = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetStringAsync("http://example.test"));
            route.ReportFailure(failure);
        }
        using (proxy.BeginRequest()) Assert.Equal("fallback", await client.GetStringAsync("http://example.test"));
    }

    [Fact]
    public void DirectRedirect_DoesNotMarkPreviousProxyAsFailed()
    {
        RefreshableWebProxy proxy = Create(() => new("localhost:8101;localhost:8102", "direct.test"));
        using RefreshableWebProxy.RequestScope request = proxy.BeginRequest();
        Assert.Equal(new Uri("http://localhost:8101"), proxy.GetProxy(new("https://example.test")));
        Assert.Null(proxy.GetProxy(new("https://direct.test")));
        request.ReportFailure(new HttpRequestException(HttpRequestError.ConnectionError, "connection failed", new SocketException((int)SocketError.ConnectionRefused)));
        Assert.Equal(new Uri("http://localhost:8101"), proxy.GetProxy(new("https://example.test")));
    }

    [Theory]
    [InlineData(HttpRequestError.ConnectionError, true)]
    [InlineData(HttpRequestError.NameResolutionError, true)]
    [InlineData(HttpRequestError.SecureConnectionError, false)]
    [InlineData(HttpRequestError.HttpProtocolError, false)]
    [InlineData(HttpRequestError.Unknown, false)]
    public void OnlyProxyConnectionFailures_AdvanceToAnotherCandidate(HttpRequestError error, bool shouldAdvance)
    {
        RefreshableWebProxy proxy = Create(() => new("localhost:8101;localhost:8102"));
        using RefreshableWebProxy.RequestScope request = proxy.BeginRequest();
        Assert.Equal(new Uri("http://localhost:8101"), proxy.GetProxy(new("https://example.test")));
        request.ReportFailure(new HttpRequestException(error, "request failed", new SocketException((int)SocketError.HostNotFound)));
        Assert.Equal(new Uri($"http://localhost:{(shouldAdvance ? 8102 : 8101)}"), proxy.GetProxy(new("https://example.test")));
    }

    [Fact]
    public async Task NativePac_UsesUpdatedScriptUrlAndHonorsDirect()
    {
        await using LoopbackServer origin = new("direct");
        await using LoopbackServer first = new("first");
        await using LoopbackServer second = new("second");
        string Script(int port) => $"function FindProxyForURL(url, host) {{ if (url.indexOf('/direct') >= 0) return 'DIRECT'; return 'PROXY 127.0.0.1:{port}'; }}";
        await using LoopbackServer firstScript = new(Script(first.Address.Port), contentType: "application/x-ns-proxy-autoconfig");
        await using LoopbackServer secondScript = new(Script(second.Address.Port), contentType: "application/x-ns-proxy-autoconfig");
        WindowsProxySettings settings = new(AutoConfigUrl: new Uri(firstScript.Address, "proxy.pac").AbsoluteUri);
        using WindowsAutomaticProxyResolver resolver = new();
        RefreshableWebProxy proxy = new(() => new(), () => settings, resolver);
        using HttpClient client = Client(proxy);
        Uri externalDestination = new("http://pac-target.invalid/battle");
        AutomaticProxyResult initial = resolver.Resolve(settings, externalDestination);
        Assert.True(initial.Success, $"Native PAC failed: Win32={initial.ErrorCode}, scriptRequests={firstScript.Requests.Count}");
        Assert.Equal("first", await client.GetStringAsync(externalDestination));
        settings = new(AutoConfigUrl: new Uri(secondScript.Address, "proxy.pac").AbsoluteUri);
        Assert.Equal("second", await client.GetStringAsync(externalDestination));
        Assert.Equal("direct", await client.GetStringAsync(new Uri(origin.Address, "direct")));
        Assert.Single(first.Requests);
        Assert.Single(second.Requests);
        Assert.Single(origin.Requests);
    }

    private static RefreshableWebProxy Create(Func<WindowsProxySettings> readSettings) => new(() => new(), readSettings, new FakeAutomatic());
    private static HttpClient Client(IWebProxy proxy, bool allowTestCertificate = false) => new(new HttpClientHandler
    {
        Proxy = proxy,
        UseCookies = false,
        ServerCertificateCustomValidationCallback = allowTestCertificate ? (_, _, _, _) => true : null
    }) { Timeout = TimeSpan.FromSeconds(15) };

    private sealed class FakeAutomatic : IAutomaticProxyResolver
    {
        public AutomaticProxyResult Result;
        public int Calls;
        public AutomaticProxyResult Resolve(WindowsProxySettings settings, Uri destination) { Calls++; return Result; }
    }

    private sealed class LoopbackServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly ConcurrentBag<Task> connections = new();
        private readonly Task accept;
        private readonly string body;
        private readonly Func<Task>? beforeResponse;
        private readonly X509Certificate2? certificate;
        private readonly string contentType;
        public Uri Address { get; }
        public ConcurrentQueue<string> Requests { get; } = new();
        public TaskCompletionSource FirstRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public LoopbackServer(string body, Func<Task>? beforeResponse = null, X509Certificate2? certificate = null, string contentType = "text/plain")
        {
            this.body = body;
            this.beforeResponse = beforeResponse;
            this.certificate = certificate;
            this.contentType = contentType;
            listener.Start();
            Address = new($"{(certificate == null ? "http" : "https")}://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
            accept = AcceptAsync();
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (!stop.IsCancellationRequested) connections.Add(ServeAsync(await listener.AcceptTcpClientAsync(stop.Token)));
            }
            catch (OperationCanceledException) { }
            catch (SocketException) when (stop.IsCancellationRequested) { }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    using Stream stream = certificate == null ? client.GetStream() : new SslStream(client.GetStream());
                    if (stream is SslStream tls) await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate }, stop.Token);
                    using StreamReader reader = new(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                    string? request = await reader.ReadLineAsync(stop.Token);
                    if (request == null) return;
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(stop.Token))) { }
                    Requests.Enqueue(request);
                    FirstRequest.TrySetResult();
                    if (request.StartsWith("CONNECT ", StringComparison.Ordinal))
                    {
                        Uri target = new("http://" + request.Split(' ')[1]);
                        using TcpClient remote = new();
                        await remote.ConnectAsync(target.Host, target.Port, stop.Token);
                        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n"), stop.Token);
                        using NetworkStream remoteStream = remote.GetStream();
                        using CancellationTokenSource tunnel = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                        Task send = stream.CopyToAsync(remoteStream, tunnel.Token);
                        Task receive = remoteStream.CopyToAsync(stream, tunnel.Token);
                        await Task.WhenAny(send, receive);
                        tunnel.Cancel();
                        try { await Task.WhenAll(send, receive); } catch (OperationCanceledException) { }
                        return;
                    }
                    if (beforeResponse != null) await beforeResponse().WaitAsync(stop.Token);
                    byte[] content = Encoding.UTF8.GetBytes(body);
                    byte[] headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {content.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(headers, stop.Token);
                    await stream.WriteAsync(content, stop.Token);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                catch (IOException) { }
            }
        }

        public async ValueTask DisposeAsync()
        {
            stop.Cancel();
            listener.Stop();
            await accept;
            await Task.WhenAll(connections).WaitAsync(TimeSpan.FromSeconds(5));
            stop.Dispose();
        }
    }
}
