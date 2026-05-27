using System.Text;
using System.Text.RegularExpressions;

namespace NATConsole.Client;

/// <summary>
/// 为穿透响应加上 /t/{隧道名} 前缀，修复前端静态资源 404、白屏。
/// </summary>
public static partial class TunnelPathRewriter
{
    private static readonly string[] RewritableContentTypes =
    [
        "text/html",
        "application/javascript",
        "text/javascript",
        "text/css",
        "application/json"
    ];

    /// <summary>前端打包常见的根路径目录名（Vue/Vite/webpack）</summary>
    private static readonly string[] StaticRootSegments =
    [
        "assets", "static", "js", "css", "fonts", "img", "images", "media", "favicon.ico", "_next"
    ];

    public static bool ShouldRewrite(string? contentType) =>
        contentType is not null &&
        RewritableContentTypes.Any(t => contentType.Contains(t, StringComparison.OrdinalIgnoreCase));

    public static string RewriteLocation(string? location, string publicPrefix)
    {
        if (string.IsNullOrEmpty(location))
            return location ?? "";

        if (location.StartsWith("/", StringComparison.Ordinal))
            return PrefixPath(location, publicPrefix);

        if (Uri.TryCreate(location, UriKind.Absolute, out var uri) &&
            uri.PathAndQuery.StartsWith("/", StringComparison.Ordinal) &&
            !uri.PathAndQuery.StartsWith(publicPrefix + "/", StringComparison.OrdinalIgnoreCase))
        {
            return $"{uri.Scheme}://{uri.Authority}{PrefixPath(uri.PathAndQuery, publicPrefix)}";
        }

        return location;
    }

    public static byte[]? RewriteBody(byte[]? body, string? contentType, string publicPrefix)
    {
        if (body is null or { Length: 0 } || !ShouldRewrite(contentType))
            return body;

        var text = Encoding.UTF8.GetString(body);

        if (contentType!.Contains("html", StringComparison.OrdinalIgnoreCase))
            text = InjectBaseTag(text, publicPrefix);

        text = RewriteText(text, publicPrefix);
        return Encoding.UTF8.GetBytes(text);
    }

    /// <summary>在 HTML 注入 base，让相对路径资源走 /t/admin/...</summary>
    public static string InjectBaseTag(string html, string publicPrefix)
    {
        publicPrefix = publicPrefix.TrimEnd('/') + "/";
        if (html.Contains("<base ", StringComparison.OrdinalIgnoreCase))
            return html;

        var baseTag = $"<base href=\"{publicPrefix}\" />";

        var headMatch = HeadTagRegex().Match(html);
        if (headMatch.Success)
            return html.Insert(headMatch.Index + headMatch.Length, baseTag);

        return baseTag + html;
    }

    public static string RewriteText(string text, string publicPrefix)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        publicPrefix = publicPrefix.TrimEnd('/');

        text = AttrPathRegex().Replace(text, m =>
        {
            var path = m.Groups[3].Value;
            if (IsAlreadyPrefixed(path, publicPrefix))
                return m.Value;
            return $"{m.Groups[1]}={m.Groups[2]}{publicPrefix}/{path}";
        });

        text = CssUrlRegex().Replace(text, m =>
        {
            var path = m.Groups[2].Value;
            if (IsAlreadyPrefixed(path, publicPrefix))
                return m.Value;
            return $"url({m.Groups[1]}{publicPrefix}/{path}{m.Groups[1]})";
        });

        text = JsonUrlRegex().Replace(text, m =>
        {
            var path = m.Groups[2].Value;
            if (path == "/" || path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
                return $"{m.Groups[1]}{publicPrefix}{path}{m.Groups[1]}";
            return m.Value;
        });

        // JS/CSS 打包产物里常见的 "/assets/xxx.js"
        foreach (var seg in StaticRootSegments)
        {
            text = ReplaceQuotedRootPath(text, publicPrefix, seg);
        }

        return text;
    }

    private static string ReplaceQuotedRootPath(string text, string publicPrefix, string segment)
    {
        foreach (var quote in new[] { '"', '\'' })
        {
            var old = $"{quote}/{segment}";
            var prefix = $"{quote}{publicPrefix}/{segment}";
            if (text.Contains(old, StringComparison.Ordinal) && !text.Contains(prefix, StringComparison.Ordinal))
                text = text.Replace(old, prefix);
        }
        return text;
    }

    private static bool IsAlreadyPrefixed(string path, string publicPrefix) =>
        path.StartsWith("t/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(publicPrefix.TrimStart('/') + "/", StringComparison.OrdinalIgnoreCase);

    private static string PrefixPath(string path, string publicPrefix)
    {
        publicPrefix = publicPrefix.TrimEnd('/');
        if (path.StartsWith(publicPrefix + "/", StringComparison.OrdinalIgnoreCase) ||
            path.Equals(publicPrefix, StringComparison.OrdinalIgnoreCase))
            return path;
        return path.StartsWith("/", StringComparison.Ordinal) ? publicPrefix + path : publicPrefix + "/" + path;
    }

    [GeneratedRegex("""<head[^>]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex HeadTagRegex();

    [GeneratedRegex("""(href|src)=(['"])/([^'"]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex AttrPathRegex();

    [GeneratedRegex("""url\((['"]?)/([^'")]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex CssUrlRegex();

    [GeneratedRegex("""("url"\s*:\s*")(/[^"]*)(")""", RegexOptions.IgnoreCase)]
    private static partial Regex JsonUrlRegex();
}
