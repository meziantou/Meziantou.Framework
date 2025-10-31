namespace Meziantou.Framework.Versioning;

/// <summary>
/// Implements Semantic Version 2.0.0. <see href="https://semver.org/"/>
/// </summary>
/// <example>
/// <code>
/// // Parse a semantic version string
/// var version = SemanticVersion.Parse("1.2.3-alpha.1+build");
/// Console.WriteLine(version.Major); // 1
/// Console.WriteLine(version.Minor); // 2
/// Console.WriteLine(version.Patch); // 3
/// Console.WriteLine(version.IsPrerelease); // true
/// Console.WriteLine(version.PrereleaseLabels); // ["alpha", "1"]
/// Console.WriteLine(version.HasMetadata); // true
/// Console.WriteLine(version.Metadata); // ["build"]
/// 
/// // Create and compare versions
/// var v1 = new SemanticVersion(1, 0, 0, "alpha");
/// var v2 = new SemanticVersion(1, 0, 0);
/// Console.WriteLine(v1 &lt; v2); // true (prerelease versions have lower precedence)
/// 
/// // Get next version
/// Console.WriteLine(v2.NextPatchVersion()); // 1.0.1
/// Console.WriteLine(v2.NextMinorVersion()); // 1.1.0
/// Console.WriteLine(v2.NextMajorVersion()); // 2.0.0
/// </code>
/// </example>
// https://github.com/semver/semver/blob/master/semver.md
// https://github.com/semver/semver/blob/master/semver.svg
public sealed class SemanticVersion : IFormattable, IComparable, IComparable<SemanticVersion>, IEquatable<SemanticVersion>
#if NET7_0_OR_GREATER
    , IParsable<SemanticVersion>
    , ISpanParsable<SemanticVersion>
#endif
{
    private static readonly IReadOnlyList<string> EmptyArray = Array.Empty<string>();

    /// <summary>Creates a new semantic version with the specified major, minor, and patch numbers.</summary>
    public SemanticVersion(int major, int minor, int patch)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    /// <summary>Creates a new semantic version with the specified major, minor, patch numbers and prerelease label.</summary>
    public SemanticVersion(int major, int minor, int patch, string? prereleaseLabel)
        : this(major, minor, patch, prereleaseLabel, metadata: null)
    {
    }

    /// <summary>Creates a new semantic version with the specified major, minor, patch numbers, prerelease label, and metadata.</summary>
    public SemanticVersion(int major, int minor, int patch, string? prereleaseLabel, string? metadata)
        : this(major, minor, patch)
    {
        if (prereleaseLabel is not null)
        {
            var index = 0;
            PrereleaseLabels = ReadPrereleaseIdentifiers(prereleaseLabel.AsSpan(), ref index);
            if (index != prereleaseLabel.Length)
                throw new ArgumentException("Value is not valid", nameof(prereleaseLabel));
        }

        if (metadata is not null)
        {
            var index = 0;
            Metadata = TryReadMetadataIdentifiers(metadata.AsSpan(), ref index);
            if (index != metadata.Length)
                throw new ArgumentException("Value is not valid", nameof(metadata));
        }
    }

    /// <summary>Creates a new semantic version with the specified major, minor, patch numbers, prerelease labels, and metadata.</summary>
    public SemanticVersion(int major, int minor, int patch, IEnumerable<string>? prereleaseLabel, IEnumerable<string>? metadata)
        : this(major, minor, patch)
    {
        if (prereleaseLabel is not null)
        {
            ReadOnlyList<string>? labels = null;
            foreach (var label in prereleaseLabel)
            {
                if (label is null || !IsPrereleaseIdentifier(label.AsSpan()))
                    throw new ArgumentException($"Label '{label}' is not valid", nameof(prereleaseLabel));

                labels ??= [];
                labels.Add(label);
            }

            if (labels is not null)
            {
                labels.Freeze();
                PrereleaseLabels = labels;
            }
        }

        if (metadata is not null)
        {
            ReadOnlyList<string>? labels = null;
            foreach (var label in metadata)
            {
                if (label is null || !IsMetadataIdentifier(label.AsSpan()))
                    throw new ArgumentException($"Label '{label}' is not valid", nameof(metadata));

                labels ??= [];
                labels.Add(label);
            }

            if (labels is not null)
            {
                labels.Freeze();
                Metadata = labels;
            }
        }
    }

    /// <summary>Gets the major version number.</summary>
    public int Major { get; }

    /// <summary>Gets the minor version number.</summary>
    public int Minor { get; }

    /// <summary>Gets the patch version number.</summary>
    public int Patch { get; }

    /// <summary>Gets the prerelease labels (e.g., alpha, beta, rc.1).</summary>
    public IReadOnlyList<string> PrereleaseLabels { get; } = EmptyArray;

    /// <summary>Gets a value indicating whether this version is a prerelease version.</summary>
    public bool IsPrerelease => PrereleaseLabels != EmptyArray;

    /// <summary>Gets the build metadata labels.</summary>
    public IReadOnlyList<string> Metadata { get; } = EmptyArray;

    /// <summary>Gets a value indicating whether this version has build metadata.</summary>
    public bool HasMetadata => Metadata != EmptyArray;

    /// <summary>Formats the semantic version as a string.</summary>
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        var sb = new StringBuilder();
        sb.Append(Major);
        sb.Append('.');
        sb.Append(Minor);
        sb.Append('.');
        sb.Append(Patch);
        if (IsPrerelease)
        {
            sb.Append('-');
            var first = true;
            foreach (var label in PrereleaseLabels)
            {
                if (!first)
                {
                    sb.Append('.');
                }

                sb.Append(label);
                first = false;
            }
        }

        if (HasMetadata)
        {
            sb.Append('+');
            var first = true;
            foreach (var label in Metadata)
            {
                if (!first)
                {
                    sb.Append('.');
                }

                sb.Append(label);
                first = false;
            }
        }

        return sb.ToString();
    }

    public override string ToString()
    {
        return ToString(format: null, formatProvider: null);
    }

    public override int GetHashCode()
    {
        return SemanticVersionComparer.Instance.GetHashCode(this);
    }

    public override bool Equals(object? obj)
    {
        if (obj is SemanticVersion semver)
        {
            return Equals(semver);
        }

        return false;
    }

    /// <summary>Determines whether the specified semantic version is equal to the current version.</summary>
    public bool Equals(SemanticVersion? other)
    {
        return SemanticVersionComparer.Instance.Equals(this, other);
    }

    /// <summary>Compares the current version to a specified object and returns an integer that indicates their relative position in the sort order.</summary>
    public int CompareTo(object? obj)
    {
        if (obj is SemanticVersion semver)
        {
            return CompareTo(semver);
        }

        throw new ArgumentException("Argument must be an instance of " + nameof(SemanticVersion), nameof(obj));
    }

    /// <summary>Compares the current version to a specified semantic version and returns an integer that indicates their relative position in the sort order.</summary>
    public int CompareTo(SemanticVersion? other)
    {
        return SemanticVersionComparer.Instance.Compare(this, other);
    }

#if NET7_0_OR_GREATER
    static SemanticVersion IParsable<SemanticVersion>.Parse(string versionsString, IFormatProvider? provider) => Parse(versionsString);
    static bool IParsable<SemanticVersion>.TryParse(string? versionsString, IFormatProvider? provider, out SemanticVersion result) => TryParse(versionsString, out result);

    static SemanticVersion ISpanParsable<SemanticVersion>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s);
    static bool ISpanParsable<SemanticVersion>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out SemanticVersion result) => TryParse(s, out result);
#endif

    /// <summary>Converts the string representation of a semantic version to its <see cref="SemanticVersion"/> equivalent.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="versionString"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="versionString"/> is not a valid semantic version.</exception>
    public static SemanticVersion Parse(string versionString)
    {
        ArgumentNullException.ThrowIfNull(versionString);

        if (TryParse(versionString, out var result))
            return result;

        throw new ArgumentException("The value is not a valid semantic version", nameof(versionString));
    }

    /// <summary>Converts the span representation of a semantic version to its <see cref="SemanticVersion"/> equivalent.</summary>
    /// <exception cref="ArgumentException"><paramref name="versionString"/> is not a valid semantic version.</exception>
    public static SemanticVersion Parse(ReadOnlySpan<char> versionString)
    {
        if (TryParse(versionString, out var result))
            return result;

        throw new ArgumentException("The value is not a valid semantic version", nameof(versionString));
    }

    /// <summary>Tries to convert the span representation of a semantic version to its <see cref="SemanticVersion"/> equivalent. A return value indicates whether the conversion succeeded.</summary>
    public static bool TryParse(ReadOnlySpan<char> versionString, [NotNullWhen(returnValue: true)] out SemanticVersion? version)
    {
        // 1.2.3
        // v1.2.3
        // 1.2.3-alpha
        // 1.2.3-alpha.1
        // 1.2.3-apha.1+build
        // 1.2.3-apha.1+build.1.2
        // 1.2.3+build
        // 1.2.3+build.1.2

        version = default;
        if (versionString.IsEmpty)
            return false;

        var index = versionString[0] is 'v' or 'V' ? 1 : 0;
        if (!TryReadNumber(versionString, ref index, out var major))
            return false;

        if (!TryReadDot(versionString, ref index))
            return false;

        if (!TryReadNumber(versionString, ref index, out var minor))
            return false;

        if (!TryReadDot(versionString, ref index))
            return false;

        if (!TryReadNumber(versionString, ref index, out var patch))
            return false;

        if (!TryReadPrerelease(versionString, ref index, out var prereleaseLabels))
        {
            prereleaseLabels = null;
        }

        if (!TryReadMetadata(versionString, ref index, out var metadata))
        {
            metadata = null;
        }

        // Should be at the end of the string
        if (index != versionString.Length)
            return false;

        version = new SemanticVersion(major, minor, patch, prereleaseLabels, metadata);
        return true;
    }

    /// <summary>Tries to convert the string representation of a semantic version to its <see cref="SemanticVersion"/> equivalent. A return value indicates whether the conversion succeeded.</summary>
    public static bool TryParse([NotNullWhen(returnValue: true)] string? versionString, [NotNullWhen(returnValue: true)] out SemanticVersion? version)
    {
        if (versionString is null)
        {
            version = null;
            return false;
        }

        return TryParse(versionString.AsSpan(), out version);
    }

    private static bool TryReadDot(ReadOnlySpan<char> versionString, ref int index)
    {
        if (index < versionString.Length && versionString[index] == '.')
        {
            index++;
            return true;
        }

        return false;
    }

    private static bool TryReadPrerelease(ReadOnlySpan<char> versionString, ref int index, [NotNullWhen(returnValue: true)] out IReadOnlyList<string>? labels)
    {
        if (index < versionString.Length && versionString[index] == '-')
        {
            index++;

            labels = ReadPrereleaseIdentifiers(versionString, ref index);
            return true;
        }

        labels = null;
        return false;
    }

    private static IReadOnlyList<string> ReadPrereleaseIdentifiers(ReadOnlySpan<char> versionString, ref int index)
    {
        var result = new List<string>();
        while (true)
        {
            if (TryReadPrereleaseIdentifier(versionString, ref index, out var label))
            {
                result.Add(label);
            }

            if (!TryReadDot(versionString, ref index))
                break;
        }

        return result.Count == 0 ? EmptyArray : ReadOnlyList.From(result);
    }

    private static bool TryReadMetadata(ReadOnlySpan<char> versionString, ref int index, [NotNullWhen(returnValue: true)] out IReadOnlyList<string>? labels)
    {
        if (index < versionString.Length && versionString[index] == '+')
        {
            index++;

            labels = TryReadMetadataIdentifiers(versionString, ref index);
            return true;
        }

        labels = null;
        return false;
    }

    private static IReadOnlyList<string> TryReadMetadataIdentifiers(ReadOnlySpan<char> versionString, ref int index)
    {
        List<string>? result = null;
        while (true)
        {
            if (TryReadMetadataIdentifier(versionString, ref index, out var label))
            {
                result ??= [];
                result.Add(label);
            }

            if (!TryReadDot(versionString, ref index))
                break;
        }

        return result is null ? EmptyArray : ReadOnlyList.From(result);
    }

    private static bool IsPrereleaseIdentifier(ReadOnlySpan<char> label)
    {
        var index = 0;
        return TryReadPrereleaseIdentifier(label, ref index, out _) && index == label.Length;
    }

    private static bool IsMetadataIdentifier(ReadOnlySpan<char> label)
    {
        var index = 0;
        return TryReadMetadataIdentifier(label, ref index, out _) && index == label.Length;
    }

    private static bool TryReadPrereleaseIdentifier(ReadOnlySpan<char> versionString, ref int index, [NotNullWhen(returnValue: true)] out string? value)
    {
        var last = index;
        while (last < versionString.Length && IsValidLabelCharacter(versionString[last]))
        {
            last++;
        }

        if (last > index)
        {
            value = versionString[index..last].ToString();
            if (value[0] != '0' || value.Any(c => !IsDigit(c)))
            {
                index = last;
                return true;
            }
        }

        value = default;
        return false;

    }

    private static bool IsValidLabelCharacter(char c)
    {
        return IsLetter(c) || IsDigit(c) || IsDash(c);
    }

    private static bool TryReadMetadataIdentifier(ReadOnlySpan<char> versionString, ref int index, [NotNullWhen(returnValue: true)] out string? value)
    {
        var last = index;
        while (last < versionString.Length && IsValidLabelCharacter(versionString[last]))
        {
            last++;
        }

        if (last > index)
        {
            value = versionString[index..last].ToString();
            index = last;
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryReadNumber(ReadOnlySpan<char> versionString, ref int index, out int value)
    {
        var last = index;
        while (last < versionString.Length && IsDigit(versionString[last]))
        {
            last++;
        }

        if (last > index)
        {
            var str = versionString[index..last];
            if (str.Length == 1 && str[0] == '0')
            {
                value = 0;
                index = last;
                return true;
            }

            if (str[0] != '0' && int.TryParse(str, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                value = n;
                index = last;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool IsDigit(char c)
    {
        return c is >= '0' and <= '9';
    }

    private static bool IsLetter(char c)
    {
        return c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');
    }

    private static bool IsDash(char c)
    {
        return c == '-';
    }

    /// <summary>Determines whether two semantic versions are equal.</summary>
    public static bool operator ==(SemanticVersion? left, SemanticVersion? right) => Equals(left, right);

    /// <summary>Determines whether two semantic versions are not equal.</summary>
    public static bool operator !=(SemanticVersion? left, SemanticVersion? right) => !(left == right);

    /// <summary>Determines whether one semantic version is less than another.</summary>
    public static bool operator <(SemanticVersion? left, SemanticVersion? right) => SemanticVersionComparer.Instance.Compare(left, right) < 0;

    /// <summary>Determines whether one semantic version is less than or equal to another.</summary>
    public static bool operator <=(SemanticVersion? left, SemanticVersion? right) => SemanticVersionComparer.Instance.Compare(left, right) <= 0;

    /// <summary>Determines whether one semantic version is greater than another.</summary>
    public static bool operator >(SemanticVersion? left, SemanticVersion? right) => SemanticVersionComparer.Instance.Compare(left, right) > 0;

    /// <summary>Determines whether one semantic version is greater than or equal to another.</summary>
    public static bool operator >=(SemanticVersion? left, SemanticVersion? right) => SemanticVersionComparer.Instance.Compare(left, right) >= 0;
}
