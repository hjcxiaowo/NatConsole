using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using NATConsole.Protocol;

namespace NATConsole.Server;

public sealed class TunnelServerHost
{
    private readonly string _bindHost;
    private readonly int _controlPort;
    private readonly int _httpPort;
    private readonly string _token;
    private readonly string _publicHost;
    private readonly IReadOnlyDictionary<string, int> _dedicatedPortsByTunnel;

    private readonly ConcurrentDictionary<string, TunnelSession> _sessions = new();
    private readonly ConcurrentDictionary<int, HttpListener> _dedicatedListeners = new();
    private TcpListener? _controlListener;
    private HttpListener? _httpListener;

    public TunnelServerHost(
        string bindHost,
        int controlPort,
        int httpPort,
        string token,
        string publicHost,
        IReadOnlyDictionary<string, int>? dedicatedPortsByTunnel = null)
    {
        _bindHost = bindHost;
        _controlPort = controlPort;
        _httpPort = httpPort;
        _token = token;
        _publicHost = publicHost;
        _dedicatedPortsByTunnel = dedicatedPortsByTunnel ?? new Dictionary<string, int>();
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _controlListener = new TcpListener(IPAddress.Parse(_bindHost), _controlPort);
        _controlListener.Start();
        Console.WriteLine($"[服务端] 控制通道监听: {_bindHost}:{_controlPort}");

        _httpListener = StartHttpListener(_httpPort);
        if (_httpListener is null)
            throw new InvalidOperationException($"无法启动 HTTP 监听端口 {_httpPort}");

        Console.WriteLine($"[服务端] 路径模式入口: http://{_publicHost}:{_httpPort}/t/{{隧道名}}/");

        foreach (var (tunnelId, port) in _dedicatedPortsByTunnel)
            Console.WriteLine($"[服务端] 已配置独立端口: {tunnelId} -> http://{_publicHost}:{port}/ （根路径）");

        Console.WriteLine("[服务端] 按 Ctrl+C 停止");

        var acceptControl = AcceptControlClientsAsync(ct);
        var acceptHttp = AcceptPrefixedHttpRequestsAsync(_httpListener, ct);
        await Task.WhenAll(acceptControl, acceptHttp);
    }

    private async Task AcceptControlClientsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _controlListener!.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            _ = HandleControlClientAsync(client, ct);
        }
    }

    private async Task HandleControlClientAsync(TcpClient tcpClient, CancellationToken ct)
    {
        await using var conn = new TunnelConnection(tcpClient.GetStream());
        var remote = tcpClient.Client.RemoteEndPoint?.ToString() ?? "unknown";
        string? tunnelId = null;

        try
        {
            var first = await conn.ReceiveAsync(ct);
            if (first is not RegisterMessage register)
            {
                await conn.SendAsync(new ErrorMessage("首条消息必须是 register"), ct);
                return;
            }

            if (!string.Equals(register.Token, _token, StringComparison.Ordinal))
            {
                await conn.SendAsync(new RegisterAckMessage(false, null, "Token 无效"), ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(register.TunnelId))
            {
                await conn.SendAsync(new RegisterAckMessage(false, null, "隧道名不能为空"), ct);
                return;
            }

            tunnelId = register.TunnelId;
            var session = new TunnelSession(register.TunnelId, register.LocalHost, register.LocalPort, conn);
            if (!_sessions.TryAdd(register.TunnelId, session))
            {
                await conn.SendAsync(new RegisterAckMessage(false, null, $"隧道名已存在: {register.TunnelId}"), ct);
                return;
            }

            var publicUrl = BuildPublicUrl(register.TunnelId);
            await conn.SendAsync(new RegisterAckMessage(true, publicUrl, "注册成功"), ct);
            Console.WriteLine($"[服务端] 隧道上线: {register.TunnelId} -> {register.LocalHost}:{register.LocalPort} ({remote})");
            Console.WriteLine($"[服务端] 外网地址: {publicUrl}");

            if (_dedicatedPortsByTunnel.TryGetValue(register.TunnelId, out var dedicatedPort) && dedicatedPort > 0)
                EnsureDedicatedListener(dedicatedPort, register.TunnelId, ct);

            await session.RunHeartbeatAndPumpAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Console.WriteLine($"[服务端] 控制连接异常 ({remote}): {ex.Message}");
        }
        finally
        {
            if (tunnelId is not null)
            {
                _sessions.TryRemove(tunnelId, out _);
                Console.WriteLine($"[服务端] 隧道下线: {tunnelId}");
            }
            tcpClient.Dispose();
        }
    }

    private string BuildPublicUrl(string tunnelId)
    {
        if (_dedicatedPortsByTunnel.TryGetValue(tunnelId, out var port) && port > 0)
            return $"http://{_publicHost}:{port}/";
        return $"http://{_publicHost}:{_httpPort}/t/{tunnelId}/";
    }

    private void EnsureDedicatedListener(int port, string tunnelId, CancellationToken ct)
    {
        if (_dedicatedListeners.ContainsKey(port))
            return;

        var listener = StartHttpListener(port);
        if (listener is null)
        {
            Console.WriteLine($"[服务端] 警告: 独立端口 {port}（隧道 {tunnelId}）启动失败，请检查安全组与端口占用");
            return;
        }

        if (!_dedicatedListeners.TryAdd(port, listener))
        {
            listener.Close();
            return;
        }

        Console.WriteLine($"[服务端] 独立端口已监听: http://{_publicHost}:{port}/ -> 隧道 {tunnelId}");
        _ = AcceptDedicatedHttpRequestsAsync(listener, tunnelId, ct);
    }

    private async Task AcceptPrefixedHttpRequestsAsync(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync();
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (ct.IsCancellationRequested)
            {
                break;
            }

            _ = HandlePrefixedHttpRequestAsync(ctx, ct);
        }
    }

    private async Task AcceptDedicatedHttpRequestsAsync(HttpListener listener, string tunnelId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync();
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (ct.IsCancellationRequested)
            {
                break;
            }

            _ = HandleDedicatedHttpRequestAsync(ctx, tunnelId, ct);
        }
    }

    private async Task HandlePrefixedHttpRequestAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var request = ctx.Request;
        var (tunnelId, forwardPath, forwardQuery) = ResolvePrefixedRoute(request.Url!);

        TunnelSession? session = null;
        if (tunnelId is not null)
            _sessions.TryGetValue(tunnelId, out session);
        else if (_sessions.Count == 1)
        {
            session = _sessions.Values.First();
            forwardPath = request.Url!.AbsolutePath;
            forwardQuery = request.Url.Query;
        }

        if (session is null)
        {
            await WriteTextResponseAsync(ctx.Response, 404,
                tunnelId is null
                    ? "没有在线隧道。请用客户端连接，或访问 /t/{隧道名}/..."
                    : $"隧道不存在: {tunnelId}");
            return;
        }

        await ProxyToSessionAsync(ctx, session, forwardPath, forwardQuery, ct);
    }

    private async Task HandleDedicatedHttpRequestAsync(HttpListenerContext ctx, string tunnelId, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(tunnelId, out var session))
        {
            await WriteTextResponseAsync(ctx.Response, 404, $"隧道未连接: {tunnelId}");
            return;
        }

        var url = ctx.Request.Url!;
        await ProxyToSessionAsync(ctx, session, url.AbsolutePath, url.Query, ct);
    }

    private async Task ProxyToSessionAsync(
        HttpListenerContext ctx,
        TunnelSession session,
        string forwardPath,
        string forwardQuery,
        CancellationToken ct)
    {
        try
        {
            var request = ctx.Request;
            var response = ctx.Response;

            byte[]? body = null;
            if (request.HasEntityBody)
            {
                using var ms = new MemoryStream();
                await request.InputStream.CopyToAsync(ms, ct);
                body = ms.ToArray();
            }

            var headers = request.Headers.AllKeys
                .Where(k => k is not null && !IsHopByHopHeader(k))
                .ToDictionary(k => k!, k => request.Headers[k]!);

            var proxyRequest = new HttpProxyRequestMessage(
                Guid.NewGuid(),
                request.HttpMethod,
                forwardPath,
                forwardQuery,
                headers,
                body);

            var proxyResponse = await session.ProxyHttpAsync(proxyRequest, ct);
            if (proxyResponse is null)
            {
                await WriteTextResponseAsync(response, 502, "隧道无响应或已断开");
                return;
            }

            response.StatusCode = proxyResponse.StatusCode;
            foreach (var (key, value) in proxyResponse.Headers)
            {
                if (!IsHopByHopHeader(key))
                    response.Headers[key] = value;
            }

            if (proxyResponse.Body is { Length: > 0 })
            {
                response.ContentLength64 = proxyResponse.Body.Length;
                await response.OutputStream.WriteAsync(proxyResponse.Body, ct);
            }

            response.Close();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            try
            {
                await WriteTextResponseAsync(ctx.Response, 500, ex.Message);
            }
            catch
            {
                // ignored
            }
        }
    }

    private static (string? TunnelId, string Path, string Query) ResolvePrefixedRoute(Uri url)
    {
        var path = url.AbsolutePath;
        const string prefix = "/t/";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return (null, path, url.Query);

        var rest = path[prefix.Length..];
        var slash = rest.IndexOf('/');
        if (slash < 0)
            return (rest, "/", url.Query);

        var tunnelId = rest[..slash];
        var forwardPath = rest[slash..];
        return (tunnelId, forwardPath, url.Query);
    }

    private HttpListener? StartHttpListener(int port)
    {
        var prefixes = new[] { $"http://+:{port}/", $"http://{_bindHost}:{port}/", $"http://127.0.0.1:{port}/" };
        foreach (var prefix in prefixes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            try
            {
                listener.Start();
                return listener;
            }
            catch (HttpListenerException)
            {
                listener.Close();
            }
        }
        return null;
    }

    private static async Task WriteTextResponseAsync(HttpListenerResponse response, int status, string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        response.StatusCode = status;
        response.ContentType = "text/plain; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static bool IsHopByHopHeader(string name) =>
        name.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("TE", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Trailer", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Proxy-Authenticate", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase);

    private sealed class TunnelSession
    {
        private readonly TunnelConnection _connection;
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<HttpProxyResponseMessage>> _pending = new();

        public string TunnelId { get; }
        public string LocalHost { get; }
        public int LocalPort { get; }

        public TunnelSession(string tunnelId, string localHost, int localPort, TunnelConnection connection)
        {
            TunnelId = tunnelId;
            LocalHost = localHost;
            LocalPort = localPort;
            _connection = connection;
        }

        public async Task RunHeartbeatAndPumpAsync(CancellationToken ct)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(25));
            var readTask = ReadLoopAsync(ct);
            var pingTask = PingLoopAsync(timer, ct);
            await Task.WhenAny(readTask, pingTask);
            throw new IOException("隧道连接已关闭");
        }

        private async Task PingLoopAsync(PeriodicTimer timer, CancellationToken ct)
        {
            while (await timer.WaitForNextTickAsync(ct))
                await _connection.SendAsync(new PingMessage(), ct);
        }

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var msg = await _connection.ReceiveAsync(ct);
                if (msg is null)
                    break;

                switch (msg)
                {
                    case HttpProxyResponseMessage response:
                        if (_pending.TryRemove(response.RequestId, out var tcs))
                            tcs.TrySetResult(response);
                        break;
                    case PongMessage:
                        break;
                    case ErrorMessage error:
                        Console.WriteLine($"[服务端] 隧道 {TunnelId} 错误: {error.Message}");
                        break;
                }
            }
        }

        public async Task<HttpProxyResponseMessage?> ProxyHttpAsync(HttpProxyRequestMessage request, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<HttpProxyResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pending.TryAdd(request.RequestId, tcs))
                return null;

            try
            {
                await _connection.SendAsync(request, ct);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(120));
                return await tcs.Task.WaitAsync(timeout.Token);
            }
            catch
            {
                _pending.TryRemove(request.RequestId, out _);
                return null;
            }
        }
    }
}
