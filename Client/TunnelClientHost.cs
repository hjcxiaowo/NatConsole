using System.Net;
using System.Net.Sockets;
using NATConsole.Protocol;

namespace NATConsole.Client;

public sealed class TunnelClientHost
{
    private readonly string _serverHost;
    private readonly int _serverPort;
    private readonly string _token;
    private readonly string _tunnelId;
    private readonly string _localHost;
    private readonly int _localPort;
    private readonly string? _forwardHost;
    private readonly string _publicPathPrefix;
    private readonly bool _rewriteResponsePaths;

    public TunnelClientHost(
        string serverHost,
        int serverPort,
        string token,
        string tunnelId,
        string localHost,
        int localPort,
        string? forwardHost = null,
        bool rewriteResponsePaths = true)
    {
        _serverHost = serverHost;
        _serverPort = serverPort;
        _token = token;
        _tunnelId = tunnelId;
        _localHost = localHost;
        _localPort = localPort;
        _forwardHost = forwardHost;
        _publicPathPrefix = $"/t/{tunnelId.Trim('/')}";
        _rewriteResponsePaths = rewriteResponsePaths;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndServeAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[客户端] 连接断开: {ex.Message}，5 秒后重连...");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task ConnectAndServeAsync(CancellationToken ct)
    {
        using var tcp = new TcpClient();
        Console.WriteLine($"[客户端] 正在连接 {_serverHost}:{_serverPort} ...");
        await tcp.ConnectAsync(_serverHost, _serverPort, ct);

        await using var conn = new TunnelConnection(tcp.GetStream());
        await conn.SendAsync(new RegisterMessage(_token, _tunnelId, _localHost, _localPort), ct);

        var ack = await conn.ReceiveAsync(ct);
        if (ack is not RegisterAckMessage registerAck)
            throw new InvalidOperationException("服务端未返回注册确认");

        if (!registerAck.Success)
            throw new InvalidOperationException(registerAck.Message ?? "注册失败");

        Console.WriteLine($"[客户端] 注册成功");
        Console.WriteLine($"[客户端] 公网访问地址: {registerAck.PublicBaseUrl}");
        Console.WriteLine($"[客户端] 本地服务: http://{_localHost}:{_localPort}");
        Console.WriteLine($"[客户端] 转发 Host 头: {GetForwardHostHeader()}");
        if (_rewriteResponsePaths)
            Console.WriteLine($"[客户端] 响应路径重写: {_publicPathPrefix}");
        Console.WriteLine("[客户端] 按 Ctrl+C 停止");

        using var http = new HttpClient(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(30),
            AutomaticDecompression = DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromSeconds(120),
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(25));
        var readTask = ReadLoopAsync(conn, http, ct);
        var pingTask = PingLoopAsync(conn, timer, ct);
        await Task.WhenAny(readTask, pingTask);
        throw new IOException("与服务端的连接已关闭");
    }

    private static async Task PingLoopAsync(TunnelConnection conn, PeriodicTimer timer, CancellationToken ct)
    {
        while (await timer.WaitForNextTickAsync(ct))
            await conn.SendAsync(new PingMessage(), ct);
    }

    private async Task ReadLoopAsync(TunnelConnection conn, HttpClient http, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var msg = await conn.ReceiveAsync(ct);
            if (msg is null)
                break;

            switch (msg)
            {
                case HttpProxyRequestMessage request:
                    _ = HandleProxyRequestAsync(conn, http, request, ct);
                    break;
                case PongMessage:
                    break;
                case PingMessage:
                    await conn.SendAsync(new PongMessage(), ct);
                    break;
            }
        }
    }

    private static readonly HashSet<string> BlockedForwardHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host",
        "X-Forwarded-Host",
        "X-Original-Host",
        "X-Forwarded-Server",
        "Forwarded"
    };

    private string GetForwardHostHeader() => _forwardHost ?? $"{_localHost}:{_localPort}";

    private static readonly HashSet<string> ContentHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Type",
        "Content-Length",
        "Content-Encoding",
        "Content-Language",
        "Content-Location",
        "Content-MD5",
        "Content-Range"
    };

    private async Task HandleProxyRequestAsync(
        TunnelConnection conn,
        HttpClient http,
        HttpProxyRequestMessage request,
        CancellationToken ct)
    {
        try
        {
            var localUri = BuildLocalUri(request);
            using var httpRequest = new HttpRequestMessage(new HttpMethod(request.Method), localUri)
            {
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact
            };

            var contentHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (key, value) in request.Headers)
            {
                if (BlockedForwardHeaders.Contains(key))
                    continue;
                if (ContentHeaderNames.Contains(key))
                {
                    contentHeaders[key] = value;
                    continue;
                }
                httpRequest.Headers.TryAddWithoutValidation(key, value);
            }

            if (request.Body is { Length: > 0 })
            {
                httpRequest.Content = new ByteArrayContent(request.Body);
                foreach (var (key, value) in contentHeaders)
                {
                    if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                        continue;
                    httpRequest.Content.Headers.TryAddWithoutValidation(key, value);
                }
            }
            else if (contentHeaders.TryGetValue("Content-Type", out var contentType))
            {
                httpRequest.Content = new ByteArrayContent([]);
                httpRequest.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            }

            httpRequest.Headers.Host = GetForwardHostHeader();

            using var response = await http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            var body = response.Content is null
                ? null
                : await response.Content.ReadAsByteArrayAsync(ct);

            var headerPairs = response.Headers.AsEnumerable();
            if (response.Content is not null)
                headerPairs = headerPairs.Concat(response.Content.Headers);

            var headers = headerPairs
                .GroupBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => string.Join(", ", g.SelectMany(x => x.Value)), StringComparer.OrdinalIgnoreCase);

            if (_rewriteResponsePaths)
            {
                if (headers.TryGetValue("Location", out var location))
                    headers["Location"] = TunnelPathRewriter.RewriteLocation(location, _publicPathPrefix);

                var contentType = headers.GetValueOrDefault("Content-Type");
                body = TunnelPathRewriter.RewriteBody(body, contentType, _publicPathPrefix);
            }

            await conn.SendAsync(new HttpProxyResponseMessage(
                request.RequestId,
                (int)response.StatusCode,
                headers,
                body), ct);
        }
        catch (Exception ex)
        {
            var errorBody = System.Text.Encoding.UTF8.GetBytes($"NATConsole 转发失败: {ex.Message}");
            await conn.SendAsync(new HttpProxyResponseMessage(
                request.RequestId,
                502,
                new Dictionary<string, string> { ["Content-Type"] = "text/plain; charset=utf-8" },
                errorBody), ct);
        }
    }

    private string BuildLocalUri(HttpProxyRequestMessage request)
    {
        var path = request.Path;
        if (string.IsNullOrEmpty(path))
            path = "/";
        var query = request.QueryString;
        if (string.IsNullOrEmpty(query))
            return $"http://{_localHost}:{_localPort}{path}";
        if (!query.StartsWith('?'))
            query = "?" + query;
        return $"http://{_localHost}:{_localPort}{path}{query}";
    }
}
