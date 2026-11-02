namespace Meziantou.Framework.SnapshotTesting;

/// <summary>
/// Identity of a snapshot whose default file name ends with a hash: <c>Stem_&lt;hash&gt;~&lt;ordinal&gt;_&lt;index&gt;</c>.
/// </summary>
/// <remarks>
/// A long stem is shortened to leave room for the rest of the file name, which depends on the extension and on the
/// number of snapshots. The files of one assertion, or of one assertion in different runs, can therefore have stems of
/// different lengths. The hash and the call ordinal never depend on the extension nor on the index, and the stem of
/// each file is a prefix of the full stem, so they tell which files belong to the assertion.
/// </remarks>
/// <param name="Stem">Name of the snapshot before it is shortened, or the shortened stem of a file name.</param>
/// <param name="Hash">The 8 lowercase hexadecimal digits of the hash.</param>
/// <param name="OrdinalPart">The <c>~&lt;ordinal&gt;</c> suffix, or an empty string for the first assertion of a test.</param>
internal sealed record SnapshotHashedName(string Stem, string Hash, string OrdinalPart)
{
    public const int HashLength = 8;

    /// <summary>Indicates whether a file name, with its stem shortened or not, belongs to this snapshot.</summary>
    public bool Matches(SnapshotHashedName fileName)
    {
        return string.Equals(Hash, fileName.Hash, StringComparison.Ordinal) &&
               string.Equals(OrdinalPart, fileName.OrdinalPart, StringComparison.Ordinal) &&
               Stem.StartsWith(fileName.Stem, StringComparison.Ordinal);
    }

    /// <summary>
    /// Parses the name of a verified file without its <c>.verified.ext</c> part. The index cannot be told from the
    /// hash when the hash only has digits (<c>Stem_12345678</c>), so a name may have two readings.
    /// </summary>
    public static void Parse(string baseName, List<SnapshotHashedName> result)
    {
        if (TryParseWithoutIndex(baseName, out var withoutIndex))
        {
            result.Add(withoutIndex);
        }

        var indexSeparator = baseName.LastIndexOf('_', StringComparison.Ordinal);
        if (indexSeparator > 0 && indexSeparator < baseName.Length - 1 && IsAsciiDigits(baseName.AsSpan(indexSeparator + 1)) &&
            TryParseWithoutIndex(baseName[..indexSeparator], out var withIndex))
        {
            result.Add(withIndex);
        }
    }

    private static bool TryParseWithoutIndex(string name, [NotNullWhen(true)] out SnapshotHashedName? result)
    {
        result = null;
        var end = name.Length;
        var ordinalSeparator = name.LastIndexOf('~', StringComparison.Ordinal);
        if (ordinalSeparator > 0 && ordinalSeparator < name.Length - 1 && IsAsciiDigits(name.AsSpan(ordinalSeparator + 1)))
        {
            end = ordinalSeparator;
        }

        // '<stem>_<hash>', with a stem of at least one character.
        var hashStart = end - HashLength;
        if (hashStart < 2 || name[hashStart - 1] != '_')
            return false;

        foreach (var c in name.AsSpan(hashStart, HashLength))
        {
            if (!char.IsAsciiHexDigitLower(c))
                return false;
        }

        result = new SnapshotHashedName(name[..(hashStart - 1)], name.Substring(hashStart, HashLength), name[end..]);
        return true;
    }

    private static bool IsAsciiDigits(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        return true;
    }
}
