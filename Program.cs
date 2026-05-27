using NATConsole;
using NATConsole.Client;
using NATConsole.Configuration;
using NATConsole.Server;

if (!CliOptions.TryParseMode(args, out var mode, out var parseError))
{
    if (parseError is not null)
    {
        Console.Error.WriteLine(parseError);
        Console.WriteLine();
    }
    CliOptions.PrintHelp();
    return parseError is not null ? 1 : 0;
}

var settings = AppConfigLoader.Load(args, out var configPath);
if (!string.IsNullOrEmpty(configPath))
    Console.WriteLine($"[配置] 使用文件: {Path.GetFullPath(configPath)}");
else
    Console.WriteLine($"[配置] {Path.Combine(AppContext.BaseDirectory, "appsettings.json")}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    switch (mode)
    {
        case RunMode.Server:
            var s = settings.Server;
            var dedicated = settings.Client.Tunnels
                .Where(t => t.DedicatedHttpPort > 0)
                .ToDictionary(t => t.Id, t => t.DedicatedHttpPort);
            Console.WriteLine($"[服务端] Token=*** PublicHost={s.PublicHost} Control={s.ControlPort} Http={s.HttpPort}");
            var server = new TunnelServerHost(s.BindHost, s.ControlPort, s.HttpPort, settings.Token, s.PublicHost, dedicated);
            await server.RunAsync(cts.Token);
            break;

        case RunMode.Client:
            var c = settings.Client;
            if (c.Tunnels.Count == 0)
            {
                Console.Error.WriteLine("[错误] Client.Tunnels 列表为空，请在 appsettings.json 中配置至少一条隧道。");
                return 1;
            }

            var pub = settings.Server.PublicHost;
            var httpPort = settings.Server.HttpPort;
            Console.WriteLine($"[客户端] 服务端 {c.ServerHost}:{c.ServerPort}，共 {c.Tunnels.Count} 条隧道");
            foreach (var t in c.Tunnels)
            {
                var url = t.DedicatedHttpPort > 0
                    ? $"http://{pub}:{t.DedicatedHttpPort}/ （根路径）"
                    : $"http://{pub}:{httpPort}/t/{t.Id}/";
                Console.WriteLine($"  - {t.Id}: {t.LocalHost}:{t.LocalPort} -> {url}");
            }
            Console.WriteLine("[客户端] 按 Ctrl+C 停止全部隧道");
            Console.WriteLine();

            var clients = c.Tunnels.Select(t => new TunnelClientHost(
                c.ServerHost,
                c.ServerPort,
                settings.Token,
                t.Id,
                t.LocalHost,
                t.LocalPort,
                t.ForwardHost,
                t.DedicatedHttpPort > 0 ? false : t.RewriteResponsePaths)).ToList();

            await Task.WhenAll(clients.Select(x => x.RunAsync(cts.Token)));
            break;
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("已停止。");
}

return 0;
