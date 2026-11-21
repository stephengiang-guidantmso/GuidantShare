using System.Text.RegularExpressions;

namespace GuidantShare.Api.Services;

/// <summary>
/// The only FileSync calls a SuperAdmin's browser may make through /api/filesync/*. Anything not listed is refused,
/// which keeps out saved-password reads, the debug dump, Swagger and file-system listings even if FileSync gains them.
/// </summary>
public static class FileSyncProxyRules
{
    private const string Id = @"[A-Za-z0-9_\-]+";

    private static readonly (string Method, Regex Path)[] Allowed =
    {
        ("GET", R("health")),
        ("GET", R("catalog")),
        ("GET", R("workflows")),
        ("POST", R("workflows")),
        ("PUT", R($"workflows/{Id}")),
        ("DELETE", R($"workflows/{Id}")),
        ("POST", R($"workflows/{Id}/(toggle|run|cancel|verify)")),
        ("GET", R($"workflows/{Id}/(browse|verifications/latest)")),
        ("GET", R($"verifications/{Id}")),
        ("GET", R($"verifications/{Id}/csv")),
        ("POST", R("sftp/verify")),
        ("GET", R("runs")),
        ("GET", R("transfers")),
        ("GET", R("transfers/count")),
        ("DELETE", R("transfers")),
        ("GET", R($"transfers/{Id}/logs")),
        ("POST", R($"transfers/{Id}/retry")),
        ("POST", R("transfers/retry-all")),
        ("GET", R("logs")),
        ("GET", R("portal-code")),
        ("POST", R("portal-code")),
        ("DELETE", R("portal-code")),
    };

    private static Regex R(string pattern) => new($"^{pattern}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>True when <paramref name="method"/> + <paramref name="path"/> (no leading slash, after /api/filesync/) may be forwarded; <paramref name="target"/> is FileSync's path.</summary>
    public static bool IsAllowed(string method, string? path, out string target)
    {
        target = "";
        if (string.IsNullOrEmpty(path) || path.Length > 200) return false;
        // No encoded characters, dot segments, backslashes or empty segments: what is matched is exactly what is sent
        if (path.Contains('%') || path.Contains('\\') || path.Contains("..") || path.Contains("//") || path.StartsWith('/') || path.EndsWith('/')) return false;
        foreach (var (m, rx) in Allowed)
        {
            if (m.Equals(method, StringComparison.OrdinalIgnoreCase) && rx.IsMatch(path))
            {
                target = "api/" + path;
                return true;
            }
        }
        return false;
    }

    /// <summary>Calls that change something are written to the admin audit log. Typing in a portal's one-time code is not (nor is its body ever stored).</summary>
    public static bool ShouldAudit(string method, string path) =>
        !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !(HttpMethods.IsPost(method) && path == "portal-code");
}
