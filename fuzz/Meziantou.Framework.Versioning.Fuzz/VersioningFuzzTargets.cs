using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Versioning.Fuzz;

/// <summary>
/// <see cref="SemanticVersion"/> accepts exactly the versions the semver.org regular expression accepts (with an optional
/// <c>v</c> prefix and components that fit an <see cref="int"/>), its parsing overloads agree, and it formats back to its
/// input. The comparison is a total order that matches the precedence rules of Semantic Versioning 2.0.0, and agrees
/// with equality, the hash code and the operators. A <see cref="SemanticVersionRange"/> parsed from NuGet or npm syntax
/// formats back to an equal range, and is satisfied exactly by the versions between its bounds.
/// </summary>
public static partial class VersioningFuzzTargets
{
    private const int MaxVersions = 6;
    private const byte RangeSyntaxNuGet = 0;
    private const byte RangeSyntaxNpm = 1;

    private static readonly string[] Tokens =
    [
        "0", "1", "9", "10", "2147483647", "2147483648", "99999999999", ".", "-", "+", "v", "V", "alpha", "beta", "rc", "0.0.0", "1.0.0", "1.2.3",
        "-0", "-00", "-a", "--", "+build", "+001", "x", "X", "*", "~", "^", ">", ">=", "<", "<=", "=", " - ", " ", "[", "]", "(", ")", ",", "||", "\t", "\n",
        "\u00A0", "\u0660", "\uFF11",
    ];

    // Sorted by precedence, from https://semver.org (section 11)
    private static readonly string[] SemVerPrecedenceExamples =
    [
        "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "2.0.0", "2.1.0", "2.1.1",
    ];

    private static readonly string[] SemVerExamples =
    [
        "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-0.3.7", "1.0.0-x.7.z.92", "1.0.0-x-y-z.--", "1.0.0-alpha+001", "1.0.0+20130313144700", "1.0.0-beta+exp.sha.5114f85",
        "1.0.0+21AF26D3----117B344092BD", "1.9.0", "1.10.0", "1.11.0", "0.1.0", "v1.2.3", "V1.2.3",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("semver-parse", Parse) { Seeds = GetParseSeeds, Dictionary = Tokens },
        new("semver-compare", Compare) { Seeds = GetCompareSeeds, Dictionary = Tokens },
        new("semver-range", Range) { Seeds = GetRangeSeeds, Dictionary = Tokens },
    ];

    /// <summary>
    /// Parses a version with every overload and checks them against the semver.org regular expression. A valid version
    /// formats back to its input (without the <c>v</c> prefix), the constructors rebuild it, and the next versions are
    /// greater stable versions.
    /// </summary>
    private static void Parse(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        var version = ParseVersion(text);

        var core = text.Length > 0 && text[0] is 'v' or 'V' ? text[1..] : text;
        var match = SemVerRegex().Match(core);
        var isValid = match.Success && FitsInt32(match.Groups["major"].Value) && FitsInt32(match.Groups["minor"].Value) && FitsInt32(match.Groups["patch"].Value);
        FuzzAssert.Equal(isValid, version is not null, $"TryParse disagrees with the semver.org regular expression on {FuzzAssert.Format(text)}");
        if (version is null)
            return;

        FuzzAssert.Equal(core, version.ToString(), "ToString does not give back the parsed text");
        FuzzAssert.Equal(match.Groups["pre"].Value, string.Join('.', version.PrereleaseLabels), "The prerelease labels differ from the regular expression");
        FuzzAssert.Equal(match.Groups["build"].Value, string.Join('.', version.Metadata), "The metadata differ from the regular expression");
        FuzzAssert.Equal(version.PrereleaseLabels.Count > 0, version.IsPrerelease, "IsPrerelease disagrees with PrereleaseLabels");
        FuzzAssert.Equal(version.Metadata.Count > 0, version.HasMetadata, "HasMetadata disagrees with Metadata");
        FuzzAssert.Equal(core, version.ToString("G", CultureInfo.InvariantCulture), "ToString(\"G\") differs from ToString()");
        FuzzAssert.Equal(core, version.ToString(format: null, formatProvider: null), "ToString(null) differs from ToString()");

        var fromLists = new SemanticVersion(version.Major, version.Minor, version.Patch, version.IsPrerelease ? version.PrereleaseLabels : null, version.HasMetadata ? version.Metadata : null);
        FuzzAssert.Equal(core, fromLists.ToString(), "The constructor taking the labels does not rebuild the version");
        var fromStrings = new SemanticVersion(version.Major, version.Minor, version.Patch, version.IsPrerelease ? string.Join('.', version.PrereleaseLabels) : null, version.HasMetadata ? string.Join('.', version.Metadata) : null);
        FuzzAssert.Equal(core, fromStrings.ToString(), "The constructor taking the label strings does not rebuild the version");

        // Build metadata does not take part in precedence (semver.org, section 10)
        var withoutMetadata = new SemanticVersion(version.Major, version.Minor, version.Patch, version.IsPrerelease ? version.PrereleaseLabels : null, metadata: null);
        CheckPair(version, fromLists);
        CheckPair(version, withoutMetadata);
        FuzzAssert.Equal(0, version.CompareTo(withoutMetadata), "Removing the metadata changes the precedence");

        CheckNextVersions(version);
    }

    /// <summary>
    /// Parses each line as a version and checks that the comparison is a total order (reflexive, antisymmetric,
    /// transitive), agrees with a reference implementation of the precedence rules of semver.org, and with
    /// <see cref="SemanticVersion.Equals(SemanticVersion)"/>, <see cref="SemanticVersion.GetHashCode"/> and the operators.
    /// </summary>
    private static void Compare(ReadOnlySpan<byte> data)
    {
        var versions = ReadVersions(data);
        foreach (var version in versions)
        {
            FuzzAssert.True(version.CompareTo(null) > 0, "A version is not greater than null");
            FuzzAssert.True(version.CompareTo((object?)null) > 0, "A version is not greater than a null object");
            FuzzAssert.True(version > null && null < version && version != null && !version.Equals(null), "The operators disagree with a null version");
        }

        foreach (var a in versions)
        {
            foreach (var b in versions)
            {
                CheckPair(a, b);
            }
        }

        foreach (var a in versions)
        {
            foreach (var b in versions)
            {
                foreach (var c in versions)
                {
                    var ab = Math.Sign(a.CompareTo(b));
                    var bc = Math.Sign(b.CompareTo(c));
                    var ac = Math.Sign(a.CompareTo(c));
                    if (ab <= 0 && bc <= 0)
                    {
                        FuzzAssert.True(ac <= 0, $"The comparison is not transitive: {a} <= {b} <= {c}, but {a} > {c}");
                        if (ab == 0 && bc == 0)
                        {
                            FuzzAssert.Equal(0, ac, $"Equality is not transitive: {a} == {b} == {c}, but {a} != {c}");
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Parses a NuGet or npm range (the first byte selects the syntax) with every overload. A range formats back to an equal
    /// range, writing its bounds as npm comparators gives an equal range, it is satisfied exactly by the versions between its
    /// bounds, and an npm range is satisfied by the versions that satisfy each of its space-separated comparators.
    /// </summary>
    private static void Range(ReadOnlySpan<byte> data)
    {
        var syntax = (byte)(FuzzInput.ReadByte(ref data) % 2);
        var text = FuzzText.Decode(FuzzInput.ReadUntil(ref data, (byte)'\n'));
        var versions = ReadVersions(data);

        var range = syntax is RangeSyntaxNpm ? ParseNpmRange(text) : ParseNuGetRange(text);
        if (range is null)
            return;

        CheckRange(range, syntax);

        var probes = new List<SemanticVersion>(versions);
        AddProbes(probes, range.MinVersion);
        AddProbes(probes, range.MaxVersion);
        foreach (var version in probes)
        {
            FuzzAssert.Equal(IsBetweenBounds(range, version), range.Satisfies(version), $"{range} does not satisfy {version} as its bounds say");
        }

        // npm comparators separated by spaces are ANDed (a hyphen range is a single comparator set)
        if (syntax is RangeSyntaxNpm && !text.Contains(" - ", StringComparison.Ordinal))
        {
            var parts = new List<SemanticVersionRange>();
            foreach (var part in text.AsSpan().Trim().ToString().Split(' '))
            {
                var trimmed = part.Trim();
                if (trimmed.Length == 0)
                    continue;

                FuzzAssert.True(SemanticVersionRange.TryParseNpm(trimmed, out var partRange), $"The comparator {FuzzAssert.Format(trimmed)} of a valid range does not parse alone");
                parts.Add(partRange);
            }

            foreach (var version in probes)
            {
                var expected = parts.TrueForAll(part => part.Satisfies(version));
                FuzzAssert.Equal(expected, range.Satisfies(version), $"{range} and the intersection of its comparators disagree on {version}");
            }
        }
    }

    private static void CheckRange(SemanticVersionRange range, byte syntax)
    {
        FuzzAssert.False(range.MinVersion is null && range.IsMinInclusive, "A missing lower bound is inclusive");
        FuzzAssert.False(range.MaxVersion is null && range.IsMaxInclusive, "A missing upper bound is inclusive");
        FuzzAssert.True(range.Equals(range) && range.Equals((object)range) && !range.Equals(null), "A range is not equal to itself");

        var comparison = range.MinVersion is not null && range.MaxVersion is not null ? Math.Sign(range.MinVersion.CompareTo(range.MaxVersion)) : -1;
        var isNuGetRange = comparison < 0 || (comparison == 0 && range.IsMinInclusive == range.IsMaxInclusive);
        if (syntax is RangeSyntaxNuGet)
        {
            FuzzAssert.True(isNuGetRange, $"ParseNuGet returned the range {range}, which has inverted or contradictory bounds");
        }

        var text = range.ToString();
        if (range.MinVersion is null && range.MaxVersion is null)
        {
            FuzzAssert.Equal("*", text, "The range without bounds does not format as '*'");
            FuzzAssert.Equal(SemanticVersionRange.All, ParseNpmRange(text), "'*' does not parse back to All");
        }
        else
        {
            var roundTripped = ParseNuGetRange(text);
            FuzzAssert.Equal(isNuGetRange, roundTripped is not null, $"ToString gives {FuzzAssert.Format(text)}, which ParseNuGet does not accept as expected");
            if (roundTripped is not null)
            {
                FuzzAssert.Equal(range, roundTripped, $"ToString {FuzzAssert.Format(text)} parses back to a different range");
                FuzzAssert.Equal(range.GetHashCode(), roundTripped.GetHashCode(), "Equal ranges have different hash codes");
                FuzzAssert.Equal(text, roundTripped.ToString(), "ToString is not a fixed point");
            }
        }

        // The same bounds written as npm comparators
        var npm = new StringBuilder();
        if (range.MinVersion is not null)
        {
            npm.Append(range.IsMinInclusive ? ">=" : ">").Append(range.MinVersion);
        }

        if (range.MaxVersion is not null)
        {
            npm.Append(npm.Length > 0 ? " " : "").Append(range.IsMaxInclusive ? "<=" : "<").Append(range.MaxVersion);
        }

        var npmText = npm.Length > 0 ? npm.ToString() : "*";
        var fromNpm = ParseNpmRange(npmText);
        FuzzAssert.True(fromNpm is not null, $"The comparators {FuzzAssert.Format(npmText)} of the range {range} do not parse");
        FuzzAssert.Equal(range, fromNpm, $"The comparators {FuzzAssert.Format(npmText)} give a different range");
    }

    private static bool IsBetweenBounds(SemanticVersionRange range, SemanticVersion version)
    {
        if (range.MinVersion is not null)
        {
            var comparison = version.CompareTo(range.MinVersion);
            if (range.IsMinInclusive ? comparison < 0 : comparison <= 0)
                return false;
        }

        if (range.MaxVersion is not null)
        {
            var comparison = version.CompareTo(range.MaxVersion);
            if (range.IsMaxInclusive ? comparison > 0 : comparison >= 0)
                return false;
        }

        return true;
    }

    // The bound, its neighbours, and the next versions, which straddle the bound
    private static void AddProbes(List<SemanticVersion> probes, SemanticVersion? bound)
    {
        if (bound is null)
            return;

        probes.Add(bound);
        probes.Add(new SemanticVersion(bound.Major, bound.Minor, bound.Patch));
        probes.Add(new SemanticVersion(bound.Major, bound.Minor, bound.Patch, "0"));
        probes.Add(new SemanticVersion(bound.Major, bound.Minor, bound.Patch, bound.IsPrerelease ? bound.PrereleaseLabels.Append("0") : null, ["probe"]));
        if (bound.Patch > 0)
        {
            probes.Add(new SemanticVersion(bound.Major, bound.Minor, bound.Patch - 1));
        }

        if (bound.Patch < int.MaxValue)
        {
            probes.Add(bound.NextPatchVersion());
        }

        if (bound.Minor < int.MaxValue)
        {
            probes.Add(bound.NextMinorVersion());
        }

        if (bound.Major < int.MaxValue)
        {
            probes.Add(bound.NextMajorVersion());
        }
    }

    private static void CheckPair(SemanticVersion a, SemanticVersion b)
    {
        var comparison = Math.Sign(a.CompareTo(b));
        FuzzAssert.Equal(-comparison, Math.Sign(b.CompareTo(a)), $"The comparison of {a} and {b} is not antisymmetric");
        FuzzAssert.Equal(ReferenceCompare(a, b), comparison, $"The comparison of {a} and {b} does not follow the semver.org precedence rules");
        FuzzAssert.Equal(comparison, Math.Sign(a.CompareTo((object)b)), $"CompareTo(object) and CompareTo(SemanticVersion) disagree on {a} and {b}");

        var isEqual = comparison == 0;
        FuzzAssert.Equal(isEqual, a.Equals(b), $"Equals disagrees with CompareTo on {a} and {b}");
        FuzzAssert.Equal(isEqual, a.Equals((object)b), $"Equals(object) disagrees with CompareTo on {a} and {b}");
        FuzzAssert.Equal(isEqual, a == b, $"== disagrees with CompareTo on {a} and {b}");
        FuzzAssert.Equal(!isEqual, a != b, $"!= disagrees with CompareTo on {a} and {b}");
        FuzzAssert.Equal(comparison < 0, a < b, $"< disagrees with CompareTo on {a} and {b}");
        FuzzAssert.Equal(comparison <= 0, a <= b, $"<= disagrees with CompareTo on {a} and {b}");
        FuzzAssert.Equal(comparison > 0, a > b, $"> disagrees with CompareTo on {a} and {b}");
        FuzzAssert.Equal(comparison >= 0, a >= b, $">= disagrees with CompareTo on {a} and {b}");
        if (isEqual)
        {
            FuzzAssert.Equal(a.GetHashCode(), b.GetHashCode(), $"The equal versions {a} and {b} have different hash codes");
        }
    }

    // The next versions are the smallest stable versions of their kind that are greater than the version
    private static void CheckNextVersions(SemanticVersion version)
    {
        var (major, minor, patch) = (version.Major, version.Minor, version.Patch);
        var isPrerelease = version.IsPrerelease;
        CheckNextVersion(version, "NextPatchVersion", SemanticVersionExtensions.NextPatchVersion, isPrerelease ? (major, minor, patch) : (major, minor, patch + 1L));
        CheckNextVersion(version, "NextMinorVersion", SemanticVersionExtensions.NextMinorVersion, isPrerelease && patch == 0 ? (major, minor, 0) : (major, minor + 1L, 0));
        CheckNextVersion(version, "NextMajorVersion", SemanticVersionExtensions.NextMajorVersion, isPrerelease && minor == 0 && patch == 0 ? (major, 0, 0) : (major + 1L, 0, 0));
    }

    private static void CheckNextVersion(SemanticVersion version, string name, Func<SemanticVersion, SemanticVersion> next, (long Major, long Minor, long Patch) expected)
    {
        var overflows = expected.Major > int.MaxValue || expected.Minor > int.MaxValue || expected.Patch > int.MaxValue;
        SemanticVersion result;
        try
        {
            result = next(version);
        }
        catch (OverflowException)
        {
            FuzzAssert.True(overflows, $"{name} of {version} throws an OverflowException");
            return;
        }

        FuzzAssert.False(overflows, $"{name} of {version} does not throw an OverflowException");
        FuzzAssert.Equal($"{expected.Major}.{expected.Minor}.{expected.Patch}", result.ToString(), $"{name} of {version} is not the smallest greater stable version of its kind");
        FuzzAssert.True(result > version, $"{name} of {version} is not greater than the version");
    }

    // Semantic Versioning 2.0.0, section 11, with the numeric identifiers compared as unbounded integers
    private static int ReferenceCompare(SemanticVersion a, SemanticVersion b)
    {
        var result = Math.Sign(a.Major.CompareTo(b.Major));
        if (result == 0)
        {
            result = Math.Sign(a.Minor.CompareTo(b.Minor));
        }

        if (result == 0)
        {
            result = Math.Sign(a.Patch.CompareTo(b.Patch));
        }

        if (result != 0)
            return result;

        var left = a.PrereleaseLabels;
        var right = b.PrereleaseLabels;
        if (left.Count == 0 || right.Count == 0)
            return Math.Sign(right.Count.CompareTo(left.Count));

        for (var i = 0; i < left.Count && i < right.Count; i++)
        {
            var isLeftNumeric = left[i].All(char.IsAsciiDigit);
            var isRightNumeric = right[i].All(char.IsAsciiDigit);
            result = (isLeftNumeric, isRightNumeric) switch
            {
                (true, true) => BigInteger.Parse(left[i], NumberStyles.None, CultureInfo.InvariantCulture).CompareTo(BigInteger.Parse(right[i], NumberStyles.None, CultureInfo.InvariantCulture)),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(left[i], right[i]),
            };

            if (result != 0)
                return Math.Sign(result);
        }

        return Math.Sign(left.Count.CompareTo(right.Count));
    }

    private static SemanticVersion? ParseVersion(string text)
    {
        SemanticVersion? parsed;
        try
        {
            parsed = SemanticVersion.Parse(text);
        }
        catch (ArgumentException)
        {
            parsed = null;
        }

        SemanticVersion? parsedSpan;
        try
        {
            parsedSpan = SemanticVersion.Parse(text.AsSpan());
        }
        catch (ArgumentException)
        {
            parsedSpan = null;
        }

        var isValid = parsed is not null;
        FuzzAssert.Equal(isValid, parsedSpan is not null, "Parse(string) and Parse(ReadOnlySpan<char>) disagree");
        FuzzAssert.Equal(isValid, SemanticVersion.TryParse(text, out var tryParsed), "Parse and TryParse(string) disagree");
        FuzzAssert.Equal(isValid, SemanticVersion.TryParse(text.AsSpan(), out var tryParsedSpan), "Parse and TryParse(ReadOnlySpan<char>) disagree");
        FuzzAssert.Equal(isValid, TryParseParsable<SemanticVersion>(text, out var parsable), "Parse and IParsable.TryParse disagree");
        FuzzAssert.Equal(isValid, TryParseSpanParsable<SemanticVersion>(text, out var spanParsable), "Parse and ISpanParsable.TryParse disagree");
        if (parsed is null)
            return null;

        var expected = parsed.ToString();
        foreach (var other in (SemanticVersion?[])[parsedSpan, tryParsed, tryParsedSpan, parsable, spanParsable])
        {
            FuzzAssert.Equal(expected, other?.ToString(), "The parsing overloads give different versions");
        }

        return parsed;
    }

    private static bool TryParseParsable<T>(string text, out T? result)
        where T : IParsable<T>
    {
        return T.TryParse(text, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParseSpanParsable<T>(string text, out T? result)
        where T : ISpanParsable<T>
    {
        return T.TryParse(text.AsSpan(), CultureInfo.InvariantCulture, out result);
    }

    private static SemanticVersionRange? ParseNuGetRange(string text)
    {
        SemanticVersionRange? parsed;
        try
        {
            parsed = SemanticVersionRange.ParseNuGet(text);
        }
        catch (FormatException)
        {
            parsed = null;
        }

        SemanticVersionRange? parsedSpan;
        try
        {
            parsedSpan = SemanticVersionRange.ParseNuGet(text.AsSpan());
        }
        catch (FormatException)
        {
            parsedSpan = null;
        }

        _ = SemanticVersionRange.TryParseNuGet(text, out var tryParsed);
        _ = SemanticVersionRange.TryParseNuGet(text.AsSpan(), out var tryParsedSpan);
        CheckSameRanges(parsed, parsedSpan, tryParsed, tryParsedSpan, "ParseNuGet");
        return parsed;
    }

    private static SemanticVersionRange? ParseNpmRange(string text)
    {
        SemanticVersionRange? parsed;
        try
        {
            parsed = SemanticVersionRange.ParseNpm(text);
        }
        catch (FormatException)
        {
            parsed = null;
        }

        SemanticVersionRange? parsedSpan;
        try
        {
            parsedSpan = SemanticVersionRange.ParseNpm(text.AsSpan());
        }
        catch (FormatException)
        {
            parsedSpan = null;
        }

        _ = SemanticVersionRange.TryParseNpm(text, out var tryParsed);
        _ = SemanticVersionRange.TryParseNpm(text.AsSpan(), out var tryParsedSpan);
        CheckSameRanges(parsed, parsedSpan, tryParsed, tryParsedSpan, "ParseNpm");
        return parsed;
    }

    private static void CheckSameRanges(SemanticVersionRange? parsed, SemanticVersionRange? parsedSpan, SemanticVersionRange? tryParsed, SemanticVersionRange? tryParsedSpan, string name)
    {
        FuzzAssert.Equal(parsed is not null, parsedSpan is not null, $"{name}(string) and {name}(ReadOnlySpan<char>) disagree");
        FuzzAssert.Equal(parsed is not null, tryParsed is not null, $"{name} and Try{name}(string) disagree");
        FuzzAssert.Equal(parsed is not null, tryParsedSpan is not null, $"{name} and Try{name}(ReadOnlySpan<char>) disagree");
        if (parsed is null)
            return;

        var expected = parsed.ToString();
        foreach (var other in (SemanticVersionRange?[])[parsedSpan, tryParsed, tryParsedSpan])
        {
            FuzzAssert.True(parsed == other && !(parsed != other), $"The {name} overloads give different ranges");
            FuzzAssert.Equal(expected, other?.ToString(), $"The {name} overloads give different ranges");
        }
    }

    private static List<SemanticVersion> ReadVersions(ReadOnlySpan<byte> data)
    {
        var versions = new List<SemanticVersion>();
        while (!data.IsEmpty && versions.Count < MaxVersions)
        {
            var line = FuzzText.Decode(FuzzInput.ReadUntil(ref data, (byte)'\n'));
            if (SemanticVersion.TryParse(line, out var version))
            {
                versions.Add(version);
            }
        }

        return versions;
    }

    private static bool FitsInt32(string value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private static IEnumerable<FuzzSeed> GetParseSeeds()
    {
        var index = 0;
        foreach (var text in SemVerExamples.Concat(GetTestStrings()).Distinct(StringComparer.Ordinal))
        {
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"parse-{index++:D3}-{text}"), text);
        }
    }

    private static IEnumerable<FuzzSeed> GetCompareSeeds()
    {
        yield return FuzzSeed.FromText("semver-precedence-1", string.Join('\n', SemVerPrecedenceExamples.Take(MaxVersions)));
        yield return FuzzSeed.FromText("semver-precedence-2", string.Join('\n', SemVerPrecedenceExamples.Skip(MaxVersions - 1)));
        yield return FuzzSeed.FromText("semver-examples", string.Join('\n', SemVerExamples.Take(MaxVersions)));

        var versions = GetTestStrings().Where(text => SemanticVersion.TryParse(text, out _)).ToArray();
        for (var i = 0; i < versions.Length; i += 3)
        {
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"tests-{i:D3}"), string.Join('\n', versions.Skip(i).Take(3)));
        }
    }

    private static IEnumerable<FuzzSeed> GetRangeSeeds()
    {
        const string Probes = "\n0.9.0\n1.0.0-alpha\n1.0.0\n1.5.0\n2.0.0\n2.0.1";
        var index = 0;
        foreach (var text in GetTestStrings())
        {
            if (text.Contains('\n', StringComparison.Ordinal))
                continue;

            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"nuget-{index:D3}-{text}"), [RangeSyntaxNuGet], text + Probes);
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"npm-{index:D3}-{text}"), [RangeSyntaxNpm], text + Probes);
            index++;
        }
    }

    // The string literals of the unit tests: versions, ranges, and a few other strings
    private static IEnumerable<string> GetTestStrings()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in (string[])["SemanticVersionTests.cs", "SemanticVersionRangeTests.cs"])
        {
            var source = FuzzResources.ReadText(typeof(VersioningFuzzTargets).Assembly, name);
            foreach (Match match in StringLiteralRegex().Matches(source))
            {
                var value = Unescape(match.Groups["value"].Value);
                if (value.Length > 0 && result.Add(value))
                    yield return value;
            }
        }
    }

    private static string Unescape(string value)
    {
        var result = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                result.Append(value[i]);
                continue;
            }

            i++;
            switch (value[i])
            {
                case 'n':
                    result.Append('\n');
                    break;
                case 'r':
                    result.Append('\r');
                    break;
                case 't':
                    result.Append('\t');
                    break;
                case '0':
                    result.Append('\0');
                    break;
                case 'u' when i + 4 < value.Length:
                    result.Append((char)int.Parse(value.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                default:
                    result.Append(value[i]);
                    break;
            }
        }

        return result.ToString();
    }

    // https://semver.org/#is-there-a-suggested-regular-expression-regex-to-check-a-semver-string, with ASCII digits
    [GeneratedRegex(@"^(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:-(?<pre>(?:0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+(?<build>[0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?\z", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex SemVerRegex();

    [GeneratedRegex(@"(?<![@$])""(?<value>(?:[^""\\\r\n]|\\.)*)""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex StringLiteralRegex();
}
