namespace Meziantou.Framework.Versioning;

/// <summary>
/// Implements Semantic Version 2.0.0. <see href="https://semver.org/"/>
/// </summary>
// https://github.com/semver/semver/blob/master/semver.md
// https://github.com/semver/semver/blob/master/semver.svg
public sealed class SemanticVersion : IFormattable, IComparable, IComparable<SemanticVersion>, IEquatable<SemanticVersion>
#if NET7_0_OR_GREATER
    , IParsable<SemanticVersion>
    , ISpanParsable<SemanticVersion>
#endif
{
    private static readonly IReadOnlyList<string> EmptyArray = Array.Empty<string>();

    public SemanticVersion(int major, int minor, int patch)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    public SemanticVersion(int major, int minor, int patch, string? prereleaseLabel)
        : this(major, minor, patch, prereleaseLabel, metadata: null)
    {
    }

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

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    public IReadOnlyList<string> PrereleaseLabels { get; } = EmptyArray;
    public bool IsPrerelease => PrereleaseLabels != EmptyArray;

    public IReadOnlyList<string> Metadata { get; } = EmptyArray;
    public bool HasMetadata => Metadata != EmptyArray;

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

    public bool Equals(SemanticVersion? other)
    {
        return SemanticVersionComparer.Instance.Equals(this, other);
    }

    public int CompareTo(object? obj)
    {
        if (obj is SemanticVersion semver)
        {
            return CompareTo(semver);
        }

        throw new ArgumentException("Argument must be an instance of " + nameof(SemanticVersion), nameof(obj));
    }

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

    public static SemanticVersion Parse(string versionString)
    {
        ArgumentNullException.ThrowIfNull(versionString);

        if (TryParse(versionString, out var result))
            return result;

        throw new ArgumentException("The value is not a valid semantic version", nameof(versionString));
    }

    public static SemanticVersion Parse(ReadOnlySpan<char> versionString)
    {
        if (TryParse(versionString, out var result))
            return result;

        throw new ArgumentException("The value is not a valid semantic version", nameof(versionString));
    }

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

#if NET7_0_OR_GREATER
            if (str[0] != '0' && int.TryParse(str, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
#else
            if (str[0] != '0' && int.TryParse(str.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var n))
#endif
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

    public static bool operator ==(SemanticVersion? left, SemanticVersion? right) => Equals(left, right);

    public static bool operator !=(SemanticVersion? left, SemanticVersion? right) => !(left == right);

    public static bool operator <(SemanticVersion? left, SemanticVersion? right) => SemanticVersionComparer.Instance.Compare(left, right) < 0;

    public static bool operator <=(SemanticVersion? left, SemanticVersion? right) => SemanticVersionComparer.Instance.Compare(left, right) <= 0;

    public static bool operator >(SemanticVersion? left, SemanticVersion? right) => SemanticVersionComparer.Instance.Compare(left, right) > 0;

    public static bool operator >=(SemanticVersion? left, SemanticVersion? right) => SemanticVersionComparer.Instance.Compare(left, right) >= 0;
}
