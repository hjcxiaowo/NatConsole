using Microsoft.Extensions.Configuration;

namespace NATConsole.Configuration;

public static class AppConfigLoader
{
    public static AppSettings Load(string[] args, out string? configPath)
    {
        ParseConfigArgs(args, out configPath, out var envName);

        var builder = new ConfigurationBuilder();

        if (!string.IsNullOrEmpty(configPath))
        {
            var full = Path.GetFullPath(configPath);
            var dir = Path.GetDirectoryName(full) ?? AppContext.BaseDirectory;
            builder.SetBasePath(dir)
                .AddJsonFile(Path.GetFileName(full)!, optional: false, reloadOnChange: false);
        }
        else
        {
            builder.SetBasePath(AppContext.BaseDirectory);

            var file = ResolveConfigFileForEnv(envName);
            builder.AddJsonFile(file, optional: false, reloadOnChange: false);
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
