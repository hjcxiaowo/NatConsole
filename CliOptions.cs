using NATConsole.Configuration;

namespace NATConsole;

public enum RunMode
{
    None,
    Server,
    Client
}

public static class CliOptions
{
    public static bool TryParseMode(string[] args, out RunMode mode, out string? error)
    {
        mode = RunMode.None;
        error = null;

        var cmdArgs = AppConfigLoader.StripConfigArgs(args);
        if (cmdArgs.Length == 0)
            return false;

        mode = cmdArgs[0].ToLowerInvariant() switch
        {
            "server" or "s" => RunMode.Server,
            "client" or "c" => RunMode.Client,
            "help" or "-h" or "--help" => RunMode.None,
            _ => RunMode.None
        };

        if (mode == RunMode.None && cmdArgs[0] is not ("help" or "-h" or "--help"))
        {
            error = $"未知命令: {cmdArgs[0]}";
            return false;
        }

        return mode != RunMode.None;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
            NATConsole - 自托管 HTTP 内网穿透

            配置：
              - 默认读取程序目录 appsettings.json
              - 可用 --config 指定任意配置文件路径
              - 可用 --env 选择环境（Development/Production），会按优先级查找：
                appsettings.{Env}.local.json -> appsettings.{Env}.json -> appsettings.{Env}.json.example -> appsettings.json

            用法:
              NATConsole server
              NATConsole client
              NATConsole client --config D:\my\appsettings.json
              NATConsole server --env prod
              NATConsole client --env dev

            客户端 Client.Tunnels 列表可配置多条隧道，一次启动全部连接。

            环境变量覆盖示例:
              NATCONSOLE_Token=xxx
              NATCONSOLE_Client__ServerHost=47.113.101.235
            """);
    }
}
