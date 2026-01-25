using System.Runtime.InteropServices;

namespace Meziantou.Framework.Versioning;

/// <summary>
/// Represents a range of semantic versions with optional lower and upper bounds.
/// Each bound can be inclusive or exclusive.
/// </summary>
/// <example>
/// <code>
/// // NuGet format examples
/// var range1 = SemanticVersionRange.ParseNuGet("[1.0.0, 2.0.0)"); // >=1.0.0 and &lt;2.0.0
/// var range2 = SemanticVersionRange.ParseNuGet("1.0.0");          // >=1.0.0
/// var range3 = SemanticVersionRange.ParseNuGet("[1.0.0]");        // ==1.0.0
///
/// // npm format examples
/// var range4 = SemanticVersionRange.ParseNpm("&gt;=1.0.0 &lt;2.0.0");  // >=1.0.0 and &lt;2.0.0
/// var range5 = SemanticVersionRange.ParseNpm("&lt;=2.0.0");           // &lt;=2.0.0
///
/// // Check if a version satisfies the range
/// var version = SemanticVersion.Parse("1.5.0");
/// Console.WriteLine(range1.Satisfies(version)); // true
/// </code>
/// </example>
[Meziantou.Analyzer.Annotations.CultureInsensitiveType]
public sealed class SemanticVersionRange : IEquatable<SemanticVersionRange>
{
    /// <summary>Gets a range that matches all versions.</summary>
    public static SemanticVersionRange All { get; } = new(minVersion: null, maxVersion: null, isMinInclusive: false, isMaxInclusive: false);

    /// <summary>Creates a new semantic version range with the specified bounds.</summary>
    /// <param name="minVersion">The minimum version bound, or null for no lower bound.</param>
    /// <param name="maxVersion">The maximum version bound, or null for no upper bound.</param>
    /// <param name="isMinInclusive">Whether the minimum bound is inclusive.</param>
    /// <param name="isMaxInclusive">Whether the maximum bound is inclusive.</param>
    public SemanticVersionRange(SemanticVersion? minVersion, SemanticVersion? maxVersion, bool isMinInclusive, bool isMaxInclusive)
    {
        MinVersion = minVersion;
        MaxVersion = maxVersion;
        IsMinInclusive = isMinInclusive;
        IsMaxInclusive = isMaxInclusive;
    }

    /// <summary>Gets the minimum version bound, or null if there is no lower bound.</summary>
    public SemanticVersion? MinVersion { get; }

    /// <summary>Gets the maximum version bound, or null if there is no upper bound.</summary>
    public SemanticVersion? MaxVersion { get; }

    /// <summary>Gets a value indicating whether the minimum bound is inclusive.</summary>
    public bool IsMinInclusive { get; }

    /// <summary>Gets a value indicating whether the maximum bound is inclusive.</summary>
    public bool IsMaxInclusive { get; }

    /// <summary>Determines whether the specified version satisfies this range.</summary>
    /// <param name="version">The version to check.</param>
    /// <returns>true if the version satisfies the range; otherwise, false.</returns>
    public bool Satisfies(SemanticVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        if (MinVersion is not null)
        {
            var comparison = version.CompareTo(MinVersion);
            if (IsMinInclusive)
            {
                if (comparison < 0)
                {
                    return false;
                }
            }
            else
            {
                if (comparison <= 0)
                {
                    return false;
                }
            }
        }

        if (MaxVersion is not null)
        {
            var comparison = version.CompareTo(MaxVersion);
            if (IsMaxInclusive)
            {
                if (comparison > 0)
                {
                    return false;
                }
            }
            else
            {
                if (comparison >= 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Creates a range that matches only the exact specified version.</summary>
    /// <param name="version">The exact version to match.</param>
    /// <returns>A range that matches only the specified version.</returns>
    public static SemanticVersionRange Exact(SemanticVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new SemanticVersionRange(version, version, isMinInclusive: true, isMaxInclusive: true);
    }

    /// <summary>Creates a range that matches versions greater than or equal to the specified version.</summary>
    /// <param name="version">The minimum version (inclusive).</param>
    /// <returns>A range that matches versions >= the specified version.</returns>
    public static SemanticVersionRange GreaterThanOrEqual(SemanticVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new SemanticVersionRange(version, maxVersion: null, isMinInclusive: true, isMaxInclusive: false);
    }

    /// <summary>Creates a range that matches versions greater than the specified version.</summary>
    /// <param name="version">The minimum version (exclusive).</param>
    /// <returns>A range that matches versions > the specified version.</returns>
    public static SemanticVersionRange GreaterThan(SemanticVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new SemanticVersionRange(version, maxVersion: null, isMinInclusive: false, isMaxInclusive: false);
    }

    /// <summary>Creates a range that matches versions less than or equal to the specified version.</summary>
    /// <param name="version">The maximum version (inclusive).</param>
    /// <returns>A range that matches versions &lt;= the specified version.</returns>
    public static SemanticVersionRange LessThanOrEqual(SemanticVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new SemanticVersionRange(minVersion: null, version, isMinInclusive: false, isMaxInclusive: true);
    }

    /// <summary>Creates a range that matches versions less than the specified version.</summary>
    /// <param name="version">The maximum version (exclusive).</param>
    /// <returns>A range that matches versions &lt; the specified version.</returns>
    public static SemanticVersionRange LessThan(SemanticVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new SemanticVersionRange(minVersion: null, version, isMinInclusive: false, isMaxInclusive: false);
    }

    public override string ToString()
    {
        if (MinVersion is null && MaxVersion is null)
        {
            return "*";
        }

        if (MinVersion is not null && MaxVersion is not null && MinVersion.Equals(MaxVersion) && IsMinInclusive && IsMaxInclusive)
        {
            return $"[{MinVersion}]";
        }

        var minBracket = IsMinInclusive ? "[" : "(";
        var maxBracket = IsMaxInclusive ? "]" : ")";
        var minStr = MinVersion?.ToString() ?? "";
        var maxStr = MaxVersion?.ToString() ?? "";

        return $"{minBracket}{minStr}, {maxStr}{maxBracket}";
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as SemanticVersionRange);
    }

    public bool Equals(SemanticVersionRange? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Equals(MinVersion, other.MinVersion)
            && Equals(MaxVersion, other.MaxVersion)
            && IsMinInclusive == other.IsMinInclusive
            && IsMaxInclusive == other.IsMaxInclusive;
    }

    public override int GetHashCode()
    {
        var hash = MinVersion?.GetHashCode() ?? 0;
        hash = (hash * 397) ^ (MaxVersion?.GetHashCode() ?? 0);
        hash = (hash * 397) ^ IsMinInclusive.GetHashCode();
        hash = (hash * 397) ^ IsMaxInclusive.GetHashCode();

        return hash;
    }

    public static bool operator ==(SemanticVersionRange? left, SemanticVersionRange? right)
    {
        if (left is null)
        {
            return right is null;
        }

        return left.Equals(right);
    }

    public static bool operator !=(SemanticVersionRange? left, SemanticVersionRange? right)
    {
        return !(left == right);
    }

    /// <summary>Parses a version range in NuGet format.</summary>
    /// <remarks>
    /// Supported formats:
    /// <list type="bullet">
    /// <item><description><c>1.0.0</c> - Minimum version inclusive (>=1.0.0)</description></item>
    /// <item><description><c>[1.0.0]</c> - Exact version (==1.0.0)</description></item>
    /// <item><description><c>(1.0.0,)</c> - Greater than (>1.0.0)</description></item>
    /// <item><description><c>[1.0.0,)</c> - Greater than or equal (>=1.0.0)</description></item>
    /// <item><description><c>(,1.0.0]</c> - Less than or equal (&lt;=1.0.0)</description></item>
    /// <item><description><c>(,1.0.0)</c> - Less than (&lt;1.0.0)</description></item>
    /// <item><description><c>[1.0.0,2.0.0]</c> - Range inclusive on both ends</description></item>
    /// <item><description><c>[1.0.0,2.0.0)</c> - Range inclusive on min, exclusive on max</description></item>
    /// <item><description><c>(1.0.0,2.0.0)</c> - Range exclusive on both ends</description></item>
    /// </list>
    /// </remarks>
    /// <param name="value">The string to parse.</param>
    /// <returns>The parsed version range.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="FormatException">The value is not a valid NuGet version range.</exception>
    public static SemanticVersionRange ParseNuGet(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (TryParseNuGet(value, out var result))
        {
            return result;
        }

        throw new FormatException($"The value '{value}' is not a valid NuGet version range.");
    }

    /// <summary>Parses a version range in NuGet format.</summary>
    /// <param name="value">The span to parse.</param>
    /// <returns>The parsed version range.</returns>
    /// <exception cref="FormatException">The value is not a valid NuGet version range.</exception>
    public static SemanticVersionRange ParseNuGet(ReadOnlySpan<char> value)
    {
        if (TryParseNuGet(value, out var result))
        {
            return result;
        }

        throw new FormatException($"The value '{value}' is not a valid NuGet version range.");
    }

    /// <summary>Attempts to parse a version range in NuGet format.</summary>
    /// <param name="value">The string to parse.</param>
    /// <param name="result">When this method returns, contains the parsed version range if the parse succeeded.</param>
    /// <returns>true if the parse succeeded; otherwise, false.</returns>
    public static bool TryParseNuGet(string? value, [NotNullWhen(true)] out SemanticVersionRange? result)
    {
        if (value is null)
        {
            result = null;
            return false;
        }

        return TryParseNuGet(value.AsSpan(), out result);
    }

    /// <summary>Attempts to parse a version range in NuGet format.</summary>
    /// <param name="value">The span to parse.</param>
    /// <param name="result">When this method returns, contains the parsed version range if the parse succeeded.</param>
    /// <returns>true if the parse succeeded; otherwise, false.</returns>
    public static bool TryParseNuGet(ReadOnlySpan<char> value, [NotNullWhen(true)] out SemanticVersionRange? result)
    {
        result = null;
        value = value.Trim();

        if (value.IsEmpty)
        {
            return false;
        }

        // Check for bracket notation
        var hasOpenBracket = value[0] is '[' or '(';
        var hasCloseBracket = value[^1] is ']' or ')';

        if (hasOpenBracket != hasCloseBracket)
        {
            return false;
        }

        if (!hasOpenBracket)
        {
            // Simple version: 1.0.0 means >=1.0.0
            if (!SemanticVersion.TryParse(value, out var minVersion))
            {
                return false;
            }

            result = GreaterThanOrEqual(minVersion);
            return true;
        }

        // Bracket notation
        var isMinInclusive = value[0] == '[';
        var isMaxInclusive = value[^1] == ']';

        // Remove brackets
        var inner = value[1..^1];
        var commaIndex = inner.IndexOf(',');

        if (commaIndex < 0)
        {
            // Exact version: [1.0.0]
            if (!SemanticVersion.TryParse(inner.Trim(), out var exactVersion))
            {
                return false;
            }

            result = new SemanticVersionRange(exactVersion, exactVersion, isMinInclusive, isMaxInclusive);
            return true;
        }

        // Range with comma
        var minPart = inner[..commaIndex].Trim();
        var maxPart = inner[(commaIndex + 1)..].Trim();

        SemanticVersion? minVersion2 = null;
        SemanticVersion? maxVersion = null;

        if (!minPart.IsEmpty)
        {
            if (!SemanticVersion.TryParse(minPart, out minVersion2))
            {
                return false;
            }
        }

        if (!maxPart.IsEmpty)
        {
            if (!SemanticVersion.TryParse(maxPart, out maxVersion))
            {
                return false;
            }
        }

        result = new SemanticVersionRange(minVersion2, maxVersion, isMinInclusive, isMaxInclusive);
        return true;
    }

    /// <summary>Parses a version range in npm format.</summary>
    /// <remarks>
    /// Supported formats:
    /// <list type="bullet">
    /// <item><description><c>1.0.0</c> - Exact version</description></item>
    /// <item><description><c>=1.0.0</c> - Exact version</description></item>
    /// <item><description><c>&gt;1.0.0</c> - Greater than</description></item>
    /// <item><description><c>&gt;=1.0.0</c> - Greater than or equal</description></item>
    /// <item><description><c>&lt;1.0.0</c> - Less than</description></item>
    /// <item><description><c>&lt;=1.0.0</c> - Less than or equal</description></item>
    /// <item><description><c>&gt;=1.0.0 &lt;2.0.0</c> - Range with multiple constraints (space-separated)</description></item>
    /// <item><description><c>~1.2.3</c> - Tilde range: allows patch-level changes (&gt;=1.2.3 &lt;1.3.0)</description></item>
    /// <item><description><c>~1.2</c> - Tilde range (&gt;=1.2.0 &lt;1.3.0)</description></item>
    /// <item><description><c>~1</c> - Tilde range (&gt;=1.0.0 &lt;2.0.0)</description></item>
    /// <item><description><c>^1.2.3</c> - Caret range: allows changes that don't modify left-most non-zero (&gt;=1.2.3 &lt;2.0.0)</description></item>
    /// <item><description><c>^0.2.3</c> - Caret range (&gt;=0.2.3 &lt;0.3.0)</description></item>
    /// <item><description><c>^0.0.3</c> - Caret range (&gt;=0.0.3 &lt;0.0.4)</description></item>
    /// <item><description><c>1.0.0 - 2.0.0</c> - Hyphen range (&gt;=1.0.0 &lt;=2.0.0)</description></item>
    /// <item><description><c>*</c> - Any version</description></item>
    /// <item><description><c>1.x</c> - X-range: any minor/patch (&gt;=1.0.0 &lt;2.0.0)</description></item>
    /// <item><description><c>1.2.x</c> - X-range: any patch (&gt;=1.2.0 &lt;1.3.0)</description></item>
    /// </list>
    /// </remarks>
    /// <param name="value">The string to parse.</param>
    /// <returns>The parsed version range.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="FormatException">The value is not a valid npm version range.</exception>
    public static SemanticVersionRange ParseNpm(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (TryParseNpm(value, out var result))
        {
            return result;
        }

        throw new FormatException($"The value '{value}' is not a valid npm version range.");
    }

    /// <summary>Parses a version range in npm format.</summary>
    /// <param name="value">The span to parse.</param>
    /// <returns>The parsed version range.</returns>
    /// <exception cref="FormatException">The value is not a valid npm version range.</exception>
    /// <seealso cref="ParseNpm(string)"/>
    public static SemanticVersionRange ParseNpm(ReadOnlySpan<char> value)
    {
        if (TryParseNpm(value, out var result))
        {
            return result;
        }

        throw new FormatException($"The value '{value}' is not a valid npm version range.");
    }

    /// <summary>Attempts to parse a version range in npm format.</summary>
    /// <param name="value">The string to parse.</param>
    /// <param name="result">When this method returns, contains the parsed version range if the parse succeeded.</param>
    /// <returns>true if the parse succeeded; otherwise, false.</returns>
    /// <seealso cref="ParseNpm(string)"/>
    public static bool TryParseNpm(string? value, [NotNullWhen(true)] out SemanticVersionRange? result)
    {
        if (value is null)
        {
            result = null;
            return false;
        }

        return TryParseNpm(value.AsSpan(), out result);
    }

    /// <summary>Attempts to parse a version range in npm format.</summary>
    /// <param name="value">The span to parse.</param>
    /// <param name="result">When this method returns, contains the parsed version range if the parse succeeded.</param>
    /// <returns>true if the parse succeeded; otherwise, false.</returns>
    /// <seealso cref="ParseNpm(string)"/>
    public static bool TryParseNpm(ReadOnlySpan<char> value, [NotNullWhen(true)] out SemanticVersionRange? result)
    {
        result = null;
        value = value.Trim();

        if (value.IsEmpty)
        {
            return false;
        }

        // Handle wildcard patterns
        if (value is "*" or "x" or "X")
        {
            result = All;
            return true;
        }

        // Check for hyphen range: "1.0.0 - 2.0.0"
        var hyphenIndex = value.IndexOf(" - ".AsSpan(), StringComparison.Ordinal);
        if (hyphenIndex >= 0)
        {
            return TryParseHyphenRange(value, hyphenIndex, out result);
        }

        SemanticVersion? minVersion = null;
        SemanticVersion? maxVersion = null;
        var isMinInclusive = false;
        var isMaxInclusive = false;

        // Split by spaces for multiple constraints
        foreach (var segment in SplitBySpace(value))
        {
            var part = segment.Trim();
            if (part.IsEmpty)
            {
                continue;
            }

            if (!TryParseNpmConstraint(part, ref minVersion, ref maxVersion, ref isMinInclusive, ref isMaxInclusive))
            {
                return false;
            }
        }

        if (minVersion is null && maxVersion is null)
        {
            return false;
        }

        result = new SemanticVersionRange(minVersion, maxVersion, isMinInclusive, isMaxInclusive);
        return true;
    }

    private static bool TryParseHyphenRange(ReadOnlySpan<char> value, int hyphenIndex, [NotNullWhen(true)] out SemanticVersionRange? result)
    {
        result = null;

        var leftPart = value[..hyphenIndex].Trim();
        var rightPart = value[(hyphenIndex + 3)..].Trim(); // Skip " - "

        if (!TryParseNpmPartialVersion(leftPart, out var leftMajor, out var leftMinor, out var leftPatch, out _))
        {
            return false;
        }

        if (!TryParseNpmPartialVersion(rightPart, out var rightMajor, out var rightMinor, out var rightPatch, out _))
        {
            return false;
        }

        var minVersion = new SemanticVersion(leftMajor, leftMinor ?? 0, leftPatch ?? 0);

        SemanticVersion maxVersion;
        bool isMaxInclusive;

        if (rightPatch.HasValue)
        {
            // Full version on right: <=X.Y.Z
            maxVersion = new SemanticVersion(rightMajor, rightMinor ?? 0, rightPatch.Value);
            isMaxInclusive = true;
        }
        else if (rightMinor.HasValue)
        {
            // Partial minor: <X.(Y+1).0
            maxVersion = new SemanticVersion(rightMajor, rightMinor.Value + 1, 0);
            isMaxInclusive = false;
        }
        else
        {
            // Only major: <(X+1).0.0
            maxVersion = new SemanticVersion(rightMajor + 1, 0, 0);
            isMaxInclusive = false;
        }

        result = new SemanticVersionRange(minVersion, maxVersion, isMinInclusive: true, isMaxInclusive);
        return true;
    }

    private static bool TryParseNpmConstraint(
        ReadOnlySpan<char> part,
        ref SemanticVersion? minVersion,
        ref SemanticVersion? maxVersion,
        ref bool isMinInclusive,
        ref bool isMaxInclusive)
    {
        // Handle tilde range: ~1.2.3
        if (part.StartsWith("~".AsSpan(), StringComparison.Ordinal))
        {
            return TryParseTildeRange(part[1..].Trim(), ref minVersion, ref maxVersion, ref isMinInclusive, ref isMaxInclusive);
        }

        // Handle caret range: ^1.2.3
        if (part.StartsWith("^".AsSpan(), StringComparison.Ordinal))
        {
            return TryParseCaretRange(part[1..].Trim(), ref minVersion, ref maxVersion, ref isMinInclusive, ref isMaxInclusive);
        }

        // Handle X-range patterns (1.x, 1.2.x, 1.*, etc.)
        if (IsXRange(part))
        {
            return TryParseXRange(part, ref minVersion, ref maxVersion, ref isMinInclusive, ref isMaxInclusive);
        }

        // Parse operator and version
        var op = NpmOperator.Exact;
        var versionStart = 0;

        if (part.StartsWith(">=".AsSpan(), StringComparison.Ordinal))
        {
            op = NpmOperator.GreaterThanOrEqual;
            versionStart = 2;
        }
        else if (part.StartsWith("<=".AsSpan(), StringComparison.Ordinal))
        {
            op = NpmOperator.LessThanOrEqual;
            versionStart = 2;
        }
        else if (part.StartsWith(">".AsSpan(), StringComparison.Ordinal))
        {
            op = NpmOperator.GreaterThan;
            versionStart = 1;
        }
        else if (part.StartsWith("<".AsSpan(), StringComparison.Ordinal))
        {
            op = NpmOperator.LessThan;
            versionStart = 1;
        }
        else if (part.StartsWith("=".AsSpan(), StringComparison.Ordinal))
        {
            op = NpmOperator.Exact;
            versionStart = 1;
        }

        var versionPart = part[versionStart..].Trim();
        if (!SemanticVersion.TryParse(versionPart, out var version))
        {
            return false;
        }

        switch (op)
        {
            case NpmOperator.Exact:
                minVersion = version;
                maxVersion = version;
                isMinInclusive = true;
                isMaxInclusive = true;
                break;
            case NpmOperator.GreaterThan:
                minVersion = version;
                isMinInclusive = false;
                break;
            case NpmOperator.GreaterThanOrEqual:
                minVersion = version;
                isMinInclusive = true;
                break;
            case NpmOperator.LessThan:
                maxVersion = version;
                isMaxInclusive = false;
                break;
            case NpmOperator.LessThanOrEqual:
                maxVersion = version;
                isMaxInclusive = true;
                break;
        }

        return true;
    }

    private enum NpmOperator
    {
        Exact,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
    }

    private static bool TryParseTildeRange(
        ReadOnlySpan<char> versionPart,
        ref SemanticVersion? minVersion,
        ref SemanticVersion? maxVersion,
        ref bool isMinInclusive,
        ref bool isMaxInclusive)
    {
        // ~1.2.3 := >=1.2.3 <1.3.0
        // ~1.2 := >=1.2.0 <1.3.0
        // ~1 := >=1.0.0 <2.0.0
        // ~0.2.3 := >=0.2.3 <0.3.0

        if (!TryParseNpmPartialVersion(versionPart, out var major, out var minor, out var patch, out _))
        {
            return false;
        }

        minVersion = new SemanticVersion(major, minor ?? 0, patch ?? 0);
        isMinInclusive = true;

        if (minor.HasValue)
        {
            // ~1.2.3 or ~1.2 -> <1.3.0
            maxVersion = new SemanticVersion(major, minor.Value + 1, 0);
        }
        else
        {
            // ~1 -> <2.0.0
            maxVersion = new SemanticVersion(major + 1, 0, 0);
        }

        isMaxInclusive = false;

        return true;
    }

    private static bool TryParseCaretRange(
        ReadOnlySpan<char> versionPart,
        ref SemanticVersion? minVersion,
        ref SemanticVersion? maxVersion,
        ref bool isMinInclusive,
        ref bool isMaxInclusive)
    {
        // ^1.2.3 := >=1.2.3 <2.0.0
        // ^0.2.3 := >=0.2.3 <0.3.0
        // ^0.0.3 := >=0.0.3 <0.0.4
        // ^1.2.x := >=1.2.0 <2.0.0
        // ^0.0.x := >=0.0.0 <0.1.0
        // ^0.0 := >=0.0.0 <0.1.0
        // ^1.x := >=1.0.0 <2.0.0
        // ^0.x := >=0.0.0 <1.0.0

        if (!TryParseNpmPartialVersion(versionPart, out var major, out var minor, out var patch, out _))
        {
            return false;
        }

        minVersion = new SemanticVersion(major, minor ?? 0, patch ?? 0);
        isMinInclusive = true;

        if (major != 0)
        {
            // ^1.x.x -> <2.0.0
            maxVersion = new SemanticVersion(major + 1, 0, 0);
        }
        else if (minor.HasValue && minor.Value != 0)
        {
            // ^0.2.x -> <0.3.0
            maxVersion = new SemanticVersion(0, minor.Value + 1, 0);
        }
        else if (patch.HasValue)
        {
            // ^0.0.3 -> <0.0.4
            maxVersion = new SemanticVersion(0, 0, patch.Value + 1);
        }
        else if (minor.HasValue)
        {
            // ^0.0 -> <0.1.0
            maxVersion = new SemanticVersion(0, 1, 0);
        }
        else
        {
            // ^0 -> <1.0.0
            maxVersion = new SemanticVersion(1, 0, 0);
        }

        isMaxInclusive = false;

        return true;
    }

    private static bool IsXRange(ReadOnlySpan<char> part)
    {
        // Check if the part contains 'x', 'X', or '*' as a version component
        foreach (var c in part)
        {
            if (c is 'x' or 'X' or '*')
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseXRange(
        ReadOnlySpan<char> part,
        ref SemanticVersion? minVersion,
        ref SemanticVersion? maxVersion,
        ref bool isMinInclusive,
        ref bool isMaxInclusive)
    {
        // 1.x := >=1.0.0 <2.0.0
        // 1.2.x := >=1.2.0 <1.3.0
        // 1.* := >=1.0.0 <2.0.0
        // * := any version (handled separately)

        if (!TryParseNpmPartialVersion(part, out var major, out var minor, out var patch, out var hasWildcard))
        {
            return false;
        }

        if (!hasWildcard)
        {
            return false;
        }

        // Full wildcard case
        if (major == 0 && !minor.HasValue && !patch.HasValue && hasWildcard)
        {
            // This shouldn't happen as "*" is handled earlier, but just in case
            minVersion = null;
            maxVersion = null;
            isMinInclusive = false;
            isMaxInclusive = false;
            return true;
        }

        minVersion = new SemanticVersion(major, minor ?? 0, patch ?? 0);
        isMinInclusive = true;

        if (minor.HasValue && !patch.HasValue)
        {
            // 1.2.x -> <1.3.0
            maxVersion = new SemanticVersion(major, minor.Value + 1, 0);
        }
        else
        {
            // 1.x -> <2.0.0
            maxVersion = new SemanticVersion(major + 1, 0, 0);
        }

        isMaxInclusive = false;

        return true;
    }

    private static bool TryParseNpmPartialVersion(
        ReadOnlySpan<char> value,
        out int major,
        out int? minor,
        out int? patch,
        out bool hasWildcard)
    {
        major = 0;
        minor = null;
        patch = null;
        hasWildcard = false;

        value = value.Trim();
        if (value.IsEmpty)
        {
            return false;
        }

        // Skip 'v' or 'V' prefix if present
        if (value[0] is 'v' or 'V')
        {
            value = value[1..];
        }

        var parts = new PartEnumerable(value);
        var partIndex = 0;

        foreach (var part in parts)
        {
            if (part.IsEmpty)
            {
                return false;
            }

            var isWildcard = part is "x" or "X" or "*";
            if (isWildcard)
            {
                hasWildcard = true;
            }

            switch (partIndex)
            {
                case 0:
                    if (isWildcard)
                    {
                        major = 0;
                        hasWildcard = true;
                    }
                    else if (!TryParseInt(part, out major))
                    {
                        return false;
                    }

                    break;
                case 1:
                    if (isWildcard)
                    {
                        hasWildcard = true;
                    }
                    else if (TryParseInt(part, out var minorValue))
                    {
                        minor = minorValue;
                    }
                    else
                    {
                        return false;
                    }

                    break;
                case 2:
                    if (isWildcard)
                    {
                        hasWildcard = true;
                    }
                    else
                    {
                        // Handle prerelease/metadata suffix: "3-alpha" or "3+build"
                        var patchPart = part;
                        var dashIndex = patchPart.IndexOfAny(['-', '+']);
                        if (dashIndex >= 0)
                        {
                            patchPart = patchPart[..dashIndex];
                        }

                        if (TryParseInt(patchPart, out var patchValue))
                        {
                            patch = patchValue;
                        }
                        else
                        {
                            return false;
                        }
                    }

                    break;
            }

            partIndex++;

            // Stop after patch version
            if (partIndex > 2)
            {
                break;
            }
        }

        return partIndex >= 1;
    }

    private static bool TryParseInt(ReadOnlySpan<char> value, out int result)
    {
#if NET7_0_OR_GREATER
        return int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out result);
#else
        return int.TryParse(value.ToString(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out result);
#endif
    }

    private static SpaceEnumerable SplitBySpace(ReadOnlySpan<char> value) => new(value);

    [StructLayout(LayoutKind.Auto)]
    private ref struct PartEnumerable
    {
        private ReadOnlySpan<char> _remaining;

        public PartEnumerable(ReadOnlySpan<char> value)
        {
            _remaining = value;
        }

        public readonly PartEnumerable GetEnumerator() => this;

        public ReadOnlySpan<char> Current { get; private set; }

        public bool MoveNext()
        {
            if (_remaining.IsEmpty)
            {
                return false;
            }

            var dotIndex = _remaining.IndexOf('.');
            if (dotIndex < 0)
            {
                Current = _remaining;
                _remaining = [];
            }
            else
            {
                Current = _remaining[..dotIndex];
                _remaining = _remaining[(dotIndex + 1)..];
            }

            return true;
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private ref struct SpaceEnumerable
    {
        private ReadOnlySpan<char> _remaining;

        public SpaceEnumerable(ReadOnlySpan<char> value)
        {
            _remaining = value;
        }

        public readonly SpaceEnumerable GetEnumerator() => this;

        public ReadOnlySpan<char> Current { get; private set; }

        public bool MoveNext()
        {
            if (_remaining.IsEmpty)
            {
                return false;
            }

            var spaceIndex = _remaining.IndexOf(' ');
            if (spaceIndex < 0)
            {
                Current = _remaining;
                _remaining = [];
            }
            else
            {
                Current = _remaining[..spaceIndex];
                _remaining = _remaining[(spaceIndex + 1)..];
            }

            return true;
        }
    }
}
