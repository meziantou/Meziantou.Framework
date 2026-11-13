using Meziantou.Framework.JavaScriptEngine;

namespace Meziantou.Framework.WebPlatform.DevTools.Runtime;

/// <summary>
/// The scripts evaluated by the clients, parsed once for the process: the clients evaluate the same scripts in each new
/// document (e.g. the injected scripts of Playwright, hundreds of kilobytes), and the same functions in each context.
/// </summary>
internal static class ScriptCache
{
    private const int MaxEntries = 512;

    private static readonly Lock Lock = new();
    private static readonly Dictionary<string, JsPreparedScript> Scripts = new(StringComparer.Ordinal);

    /// <summary>Returns the parsed script of a source text, or null when it is not valid (the caller evaluates the text to get the error).</summary>
    public static JsPreparedScript? GetOrParse(string code)
    {
        lock (Lock)
        {
            if (Scripts.TryGetValue(code, out var cached))
                return cached;
        }

        if (!JsPreparedScript.TryParse(code, url: null, 0, 0, out var script, out _))
            return null;

        lock (Lock)
        {
            // The cache is emptied when it is full: the clients use a small set of scripts, the others are evaluated once
            if (Scripts.Count >= MaxEntries)
            {
                Scripts.Clear();
            }

            Scripts[code] = script;
        }

        return script;
    }
}
