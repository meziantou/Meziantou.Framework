using System.Collections.Frozen;

namespace Meziantou.Framework.UrlPatternInternal;

/// <summary>The special schemes of the URL Standard, and the default port of each.</summary>
/// <remarks>
/// <see href="https://url.spec.whatwg.org/#special-scheme">URL Standard - Special scheme</see>
/// </remarks>
internal static class SpecialSchemes
{
    // "file" is a special scheme, but it has no default port
    private static readonly FrozenDictionary<string, string> DefaultPorts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ftp"] = "21",
        ["http"] = "80",
        ["https"] = "443",
        ["ws"] = "80",
        ["wss"] = "443",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets every special scheme.</summary>
    public static FrozenSet<string> All { get; } = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "ftp", "file", "http", "https", "ws", "wss");

    /// <summary>Determines whether the scheme is a special scheme.</summary>
    public static bool Contains(string scheme) => All.Contains(scheme);

    /// <summary>Gets the default port of the scheme, when it has one.</summary>
    public static bool TryGetDefaultPort(string scheme, [MaybeNullWhen(false)] out string port) => DefaultPorts.TryGetValue(scheme, out port);
}
