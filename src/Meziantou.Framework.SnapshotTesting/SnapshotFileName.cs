namespace Meziantou.Framework.SnapshotTesting;

/// <summary>
/// Splits the name of a snapshot file into its name and its extension. The file is shared with the approval tool, so
/// the engine and the tool always agree on which file is a verified file and which one is its actual file.
/// </summary>
/// <remarks>
/// <para>
/// A verified file is named <c>Name.verified.ext</c> and its actual file <c>Name.actual.ext</c>. The extension may
/// contain dots (<c>g.cs</c>, <c>d.ts</c>), so the marker is what separates the two parts, not the last dot.
/// </para>
/// <para>
/// The default naming never puts a marker in the name part, but files named by an earlier version, or by a custom
/// strategy, may have one: a test called <c>Parse.actual.value</c> had the verified file
/// <c>Parse.actual.value.verified.txt</c>. So a file containing <c>.verified.</c> is always a verified file, never an
/// actual one, and both kinds are split at their last marker: an extension containing a marker is far less likely
/// than a name containing one.
/// </para>
/// </remarks>
internal static class SnapshotFileName
{
    public const string VerifiedMarker = ".verified.";
    public const string ActualMarker = ".actual.";

    /// <summary>Splits the name of a verified file, <c>Name.verified.ext</c>, into <c>Name</c> and <c>ext</c>.</summary>
    public static bool TryParseVerified(ReadOnlySpan<char> fileName, out ReadOnlySpan<char> name, out ReadOnlySpan<char> extension)
    {
        return TrySplit(fileName, VerifiedMarker, out name, out extension);
    }

    /// <summary>Splits the name of an actual file, <c>Name.actual.ext</c>, into <c>Name</c> and <c>ext</c>.</summary>
    public static bool TryParseActual(ReadOnlySpan<char> fileName, out ReadOnlySpan<char> name, out ReadOnlySpan<char> extension)
    {
        if (fileName.Contains(VerifiedMarker, StringComparison.Ordinal))
        {
            name = default;
            extension = default;
            return false;
        }

        return TrySplit(fileName, ActualMarker, out name, out extension);
    }

    /// <summary>Returns the name of the actual file of a verified file, or <see langword="null" /> when the name is not the one of a verified file.</summary>
    public static string? GetActualFileName(string verifiedFileName)
    {
        if (!TryParseVerified(verifiedFileName, out var name, out var extension))
            return null;

        return string.Concat(name, ActualMarker, extension);
    }

    /// <summary>Returns the name of the verified file of an actual file, or <see langword="null" /> when the name is not the one of an actual file.</summary>
    public static string? GetVerifiedFileName(string actualFileName)
    {
        if (!TryParseActual(actualFileName, out var name, out var extension))
            return null;

        return string.Concat(name, VerifiedMarker, extension);
    }

    private static bool TrySplit(ReadOnlySpan<char> fileName, string marker, out ReadOnlySpan<char> name, out ReadOnlySpan<char> extension)
    {
        var index = fileName.LastIndexOf(marker, StringComparison.Ordinal);
        if (index <= 0 || index + marker.Length >= fileName.Length)
        {
            name = default;
            extension = default;
            return false;
        }

        name = fileName[..index];
        extension = fileName[(index + marker.Length)..];
        return true;
    }
}
