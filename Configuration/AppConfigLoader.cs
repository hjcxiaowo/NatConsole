using Microsoft.Extensions.Configuration;

namespace NATConsole.Configuration;

public static class AppConfigLoader
{
    public static AppSettings Load(string[] args, out string? configPath)
    {
        ParseConfigArgs(args, out var explicitConfigPath, out var envName);

        var builder = new ConfigurationBuilder();

        // 没有显式指定配置文件时，允许交互式选择 dev/prod：
        // - 直接回车：使用 appsettings.json
        // - 输入 dev：使用 appsettings.Development.json
        // - 输入 prod：使用 appsettings.Production.json
        if (string.IsNullOrEmpty(explicitConfigPath) && string.IsNullOrEmpty(envName))
            envName = MaybePromptEnv();

        var resolvedFileName = !string.IsNullOrEmpty(explicitConfigPath)
            ? null
            : ResolveConfigFileForEnv(envName);

        // configPath 用于输出提示信息，所以这里把“最终选择的文件”也返回出去。
        configPath = !string.IsNullOrEmpty(explicitConfigPath) ? explicitConfigPath : resolvedFileName;

        if (!string.IsNullOrEmpty(explicitConfigPath))
        {
            var full = Path.GetFullPath(explicitConfigPath);
            var dir = Path.GetDirectoryName(full) ?? AppContext.BaseDirectory;
            builder.SetBasePath(dir)
                .AddJsonFile(Path.GetFileName(full)!, optional: false, reloadOnChange: false);
        }
        else
        {
            builder.SetBasePath(AppContext.BaseDirectory);
            builder.AddJsonFile(resolvedFileName!, optional: false, reloadOnChange: false);
        }

        builder.AddEnvironmentVariables("NATCONSOLE_");

        var config = builder.Build();
        var settings = new AppSettings();
        config.Bind(settings);
        return settings;
    }

    private static void ParseConfigArgs(string[] args, out string? configPath, out string? envName)
    {
        configPath = null;
        envName = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--config" or "-c" && i + 1 < args.Length)
                configPath = args[++i];

            if (args[i] is "--env" or "-e" && i + 1 < args.Length)
                envName = args[++i];
        }
    }

    public static string[] StripConfigArgs(string[] args)
    {
        var list = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--config" or "-c" or "--env" or "-e")
            {
                i++;
                continue;
            }
            list.Add(args[i]);
        }
        return list.ToArray();
    }

    private static string? MaybePromptEnv()
    {
        // 非交互环境（例如 docker -d 无 tty）不要阻塞等待输入
        if (Console.IsInputRedirected || !Environment.UserInteractive)
            return null;

        Console.Write("配置后缀(dev/prod，回车=appsettings.json)：");
        var input = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(input))
            return null;

        var s = input.Trim().ToLowerInvariant();
        if (s is "dev" or "d")
            return "Development";
        if (s is "prod" or "p")
            return "Production";

        Console.WriteLine($"未知输入: {input}（仅支持 dev/prod，回车=appsettings.json），将回退到 appsettings.json");
        return null;
    }

    private static string ResolveConfigFileForEnv(string? envName)
    {
        // 优先级：
        // 1) --env/-e 指定
        // 2) 环境变量 NATCONSOLE_ENV / DOTNET_ENVIRONMENT / ASPNETCORE_ENVIRONMENT
        // 3) 默认 appsettings.json
        var env = NormalizeEnv(envName)
            ?? NormalizeEnv(Environment.GetEnvironmentVariable("NATCONSOLE_ENV"))
            ?? NormalizeEnv(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"))
            ?? NormalizeEnv(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"));

        if (string.IsNullOrWhiteSpace(env))
            return "appsettings.json";

        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            $"appsettings.{env}.local.json",
            $"appsettings.{env}.json",
            $"appsettings.{env}.json.example",
            "appsettings.json",
        };

        foreach (var name in candidates)
        {
            var full = Path.Combine(baseDir, name);
            if (File.Exists(full))
                return name;
        }

        // 理论上不会走到这里（最后一个候选是 appsettings.json），留作兜底
        return "appsettings.json";
    }

    private static string? NormalizeEnv(string? env)
    {
        if (string.IsNullOrWhiteSpace(env))
            return null;

        return env.Trim().ToLowerInvariant() switch
        {
            "dev" or "development" => "Development",
            "prod" or "production" => "Production",
            _ => env.Trim()
        };
    }
}
