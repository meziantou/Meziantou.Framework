namespace Meziantou.Framework.WasmEngine.Wasi.Internal;

/// <summary>The canonical paths of a <see cref="WasiFileSystem"/>: relative, <c>/</c> separators, no empty, <c>.</c> or <c>..</c> segment.</summary>
internal static class WasiPaths
{
    public static string Combine(string directory, string name) => directory.Length == 0 ? name : directory + "/" + name;

    /// <summary>Returns the parent of a path that is not the root.</summary>
    public static string GetParent(string path)
    {
        var index = path.AsSpan().LastIndexOf('/');
        return index < 0 ? "" : path[..index];
    }

    /// <summary>Returns the last segment of a path that is not the root.</summary>
    public static string GetName(string path)
    {
        var index = path.AsSpan().LastIndexOf('/');
        return index < 0 ? path : path[(index + 1)..];
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="directory"/> or is inside it.</summary>
    public static bool IsSameOrInside(string path, string directory)
    {
        if (directory.Length == 0)
            return true;

        return path.Length == directory.Length
            ? string.Equals(path, directory, StringComparison.Ordinal)
            : path.Length > directory.Length && path[directory.Length] == '/' && path.StartsWith(directory, StringComparison.Ordinal);
    }

    public static bool IsCanonical(string path)
    {
        if (path.Length == 0)
            return true;

        foreach (var segment in path.Split('/'))
        {
            if (segment is "" or "." or ".." || segment.Contains('\0', StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <summary>Converts a path given by the host (<c>/</c> or <c>\</c> separators, optional leading separator) to a canonical path.</summary>
    /// <exception cref="ArgumentException">The path has <c>.</c>, <c>..</c> or null characters.</exception>
    public static string Normalize(string path, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(path, parameterName);
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment is "." or ".." || segment.Contains('\0', StringComparison.Ordinal))
                throw new ArgumentException("The path '" + path + "' is not valid: it cannot contain '.', '..' or null characters", parameterName);
        }

        return string.Join('/', segments);
    }
}
