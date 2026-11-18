namespace Meziantou.Framework.WebPlatform.Dom.FileApi;

/// <summary>The paths of the File and Directory Entries API (https://wicg.github.io/entries-api/#names-paths).</summary>
internal static class EntriesPath
{
    /// <summary>
    /// Whether <paramref name="path"/> is a valid path (https://wicg.github.io/entries-api/#valid-path): the empty string,
    /// or names, <c>.</c> and <c>..</c> separated by <c>/</c>, optionally preceded by <c>/</c> (an absolute path). A name is
    /// not empty and does not contain NUL or <c>\</c>. As in the browsers, <c>/</c> alone is the root directory.
    /// </summary>
    public static bool IsValid(string path)
    {
        if (path.Length == 0 || path == "/")
            return true;

        var start = path[0] == '/' ? 1 : 0;
        foreach (var segment in path[start..].Split('/'))
        {
            if (segment.Length == 0 || segment.AsSpan().IndexOfAny('\0', '\\') >= 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves a valid path against the full path of a directory (https://wicg.github.io/entries-api/#resolve-a-relative-path)
    /// and normalizes it: the names of the absolute path, without <c>.</c> and <c>..</c> (the parent of the root directory is
    /// the root directory).
    /// </summary>
    public static List<string> Resolve(string directoryFullPath, string path)
    {
        var absolute = path.Length > 0 && path[0] == '/' ? path : directoryFullPath + "/" + path;
        var result = new List<string>();
        foreach (var segment in absolute.Split('/'))
        {
            switch (segment)
            {
                case "" or ".":
                    break;

                case "..":
                    if (result.Count > 0)
                    {
                        result.RemoveAt(result.Count - 1);
                    }

                    break;

                default:
                    result.Add(segment);
                    break;
            }
        }

        return result;
    }

    /// <summary>The full path of an entry (https://wicg.github.io/entries-api/#dom-filesystementry-fullpath): <c>/</c> followed by its names separated by <c>/</c>.</summary>
    public static string ToFullPath(IReadOnlyList<string> names) => "/" + string.Join('/', names);
}
