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

            配置：编辑程序目录 appsettings.json（唯一配置文件）

            用法:
              NATConsole server
              NATConsole client
              NATConsole client --config D:\my\appsettings.json

            客户端 Client.Tunnels 列表可配置多条隧道，一次启动全部连接。

            环境变量覆盖示例:
              NATCONSOLE_Token=xxx
              NATCONSOLE_Client__ServerHost=47.113.101.235
            """);
    }
}
