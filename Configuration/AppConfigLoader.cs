using Microsoft.Extensions.Configuration;

namespace NATConsole.Configuration;

public static class AppConfigLoader
{
    public static AppSettings Load(string[] args, out string? configPath)
    {
        ParseConfigArgs(args, out configPath);

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
            builder.SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
        }

        builder.AddEnvironmentVariables("NATCONSOLE_");

        var config = builder.Build();
        var settings = new AppSettings();
        config.Bind(settings);
        return settings;
    }

    private static void ParseConfigArgs(string[] args, out string? configPath)
    {
        configPath = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--config" or "-c" && i + 1 < args.Length)
                configPath = args[++i];
        }
    }

    public static string[] StripConfigArgs(string[] args)
    {
        var list = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--config" or "-c")
            {
                i++;
                continue;
            }
            list.Add(args[i]);
        }
        return list.ToArray();
    }
}
