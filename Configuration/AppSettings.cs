namespace NATConsole.Configuration;

public sealed class AppSettings
{
    public string Token { get; set; } = "change-me";
    public ServerSettings Server { get; set; } = new();
    public ClientSettings Client { get; set; } = new();
}

public sealed class ServerSettings
{
    public string BindHost { get; set; } = "0.0.0.0";
    public int ControlPort { get; set; } = 7000;
    public int HttpPort { get; set; } = 8080;
    public string PublicHost { get; set; } = "127.0.0.1";
}

public sealed class ClientSettings
{
    public string ServerHost { get; set; } = "127.0.0.1";
    public int ServerPort { get; set; } = 7000;
    public List<TunnelEntry> Tunnels { get; set; } = [];
}

public sealed class TunnelEntry
{
    public string Id { get; set; } = "dev";
    public string LocalHost { get; set; } = "127.0.0.1";
    public int LocalPort { get; set; } = 5000;
    public string? ForwardHost { get; set; }
  /// <summary>
  /// 为 true 时重写响应路径（/t/隧道名/ 前缀模式）。独立端口根路径映射时应设为 false。
  /// </summary>
    public bool RewriteResponsePaths { get; set; } = true;
    /// <summary>
    /// 独立公网 HTTP 端口，映射到站点根路径 /（方案 A）。为 0 则仅用 HttpPort + /t/Id/ 前缀。
    /// </summary>
    public int DedicatedHttpPort { get; set; }
}
