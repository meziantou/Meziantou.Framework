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

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        return obj is SemanticVersionRange other && Equals(other);
    }

    public bool Equals([NotNullWhen(true)] SemanticVersionRange? other)
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

    public override int GetHashCode() => HashCode.Combine(MinVersion, MaxVersion, IsMinInclusive, IsMaxInclusive);

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
        var constraintCount = 0;
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

            constraintCount++;
        }

        // A constraint can legitimately leave both bounds open ("x.x" matches everything), so the
        // number of constraints read is what tells us whether anything was understood.
        if (constraintCount == 0)
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

        if (!TryParseNpmPartialVersion(leftPart, out var left) || left.Major is null)
        {
            return false;
        }

        if (!TryParseNpmPartialVersion(rightPart, out var right) || right.Major is null)
        {
            return false;
        }

        // LowerBound keeps any prerelease and metadata labels: "1.0.0-alpha - 2.0.0" starts at
        // 1.0.0-alpha, not at 1.0.0.
        var minVersion = left.LowerBound;

        SemanticVersion maxVersion;
        bool isMaxInclusive;

        if (right.Patch is not null)
        {
            // Full version on right: <=X.Y.Z
            maxVersion = right.LowerBound;
            isMaxInclusive = true;
        }
        else if (right.Minor is { } rightMinor)
        {
            // Partial minor: <X.(Y+1).0
            maxVersion = new SemanticVersion(right.Major.Value, rightMinor + 1, 0);
            isMaxInclusive = false;
        }
        else
        {
            // Only major: <(X+1).0.0
            maxVersion = new SemanticVersion(right.Major.Value + 1, 0, 0);
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

        // Only look for an X-range once the operator has been stripped, and only in the numeric
        // core of the version: a prerelease or metadata label may legitimately contain an 'x'
        // (">=1.0.0-exp"), and treating that as a wildcard rejects a perfectly valid constraint.
        if (IsXRange(versionPart))
        {
            // npm only gives a wildcard a meaning of its own when it stands without a comparison
            // operator, so ">=1.x" stays unsupported rather than being guessed at.
            if (op is not NpmOperator.Exact)
            {
                return false;
            }

            return TryParseXRange(versionPart, ref minVersion, ref maxVersion, ref isMinInclusive, ref isMaxInclusive);
        }

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

        if (!TryParseNpmPartialVersion(versionPart, out var partial) || partial.Major is null)
        {
            return false;
        }

        minVersion = partial.LowerBound;
        isMinInclusive = true;

        if (partial.Minor is { } minor)
        {
            // ~1.2.3 or ~1.2 -> <1.3.0
            maxVersion = new SemanticVersion(partial.Major.Value, minor + 1, 0);
        }
        else
        {
            // ~1 or ~1.x -> <2.0.0
            maxVersion = new SemanticVersion(partial.Major.Value + 1, 0, 0);
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

        if (!TryParseNpmPartialVersion(versionPart, out var partial) || partial.Major is not { } major)
        {
            return false;
        }

        minVersion = partial.LowerBound;
        isMinInclusive = true;

        maxVersion = (major, partial.Minor, partial.Patch) switch
        {
            ( > 0, _, _) => new SemanticVersion(major + 1, 0, 0),            // ^1.2.3 -> <2.0.0
            (0, > 0 and { } minor, _) => new SemanticVersion(0, minor + 1, 0), // ^0.2.3 -> <0.3.0
            (0, _, { } patch) => new SemanticVersion(0, 0, patch + 1),       // ^0.0.3 -> <0.0.4
            (0, not null, null) => new SemanticVersion(0, 1, 0),             // ^0.0 -> <0.1.0
            _ => new SemanticVersion(1, 0, 0),                               // ^0 or ^0.x -> <1.0.0
        };

        isMaxInclusive = false;

        return true;
    }

    private static bool IsXRange(ReadOnlySpan<char> version)
    {
        // A wildcard is a whole component of the numeric core. Scanning the raw characters instead
        // would treat the 'x' in a label such as "1.0.0-exp" as a wildcard.
        foreach (var component in new PartEnumerable(GetVersionCore(version)))
        {
            if (component is "x" or "X" or "*")
            {
                return true;
            }
        }

        return false;
    }

    // The numeric "major.minor.patch" part, without the prerelease or metadata labels.
    private static ReadOnlySpan<char> GetVersionCore(ReadOnlySpan<char> version)
    {
        var labelIndex = version.IndexOfAny('-', '+');
        return labelIndex < 0 ? version : version[..labelIndex];
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

        if (!TryParseNpmPartialVersion(part, out var partial) || !partial.HasWildcard)
        {
            return false;
        }

        // The major itself is a wildcard ("x", "x.x", "*"): every version matches. This is
        // distinct from a literal zero major such as "0.x", which the caller must not confuse
        // with it.
        if (partial.Major is not { } major)
        {
            minVersion = null;
            maxVersion = null;
            isMinInclusive = false;
            isMaxInclusive = false;
            return true;
        }

        minVersion = partial.LowerBound;
        isMinInclusive = true;

        if (partial.Minor is { } minor)
        {
            // 1.2.x -> <1.3.0
            maxVersion = new SemanticVersion(major, minor + 1, 0);
        }
        else
        {
            // 1.x -> <2.0.0
            maxVersion = new SemanticVersion(major + 1, 0, 0);
        }

        isMaxInclusive = false;

        return true;
    }

    // A version with optional trailing components, as npm range syntax allows: "1", "1.2", "1.2.3",
    // "1.x" and "1.2.3-beta.1" are all accepted. A null component is one that was absent or was a
    // wildcard; HasWildcard tells the two apart.
    [StructLayout(LayoutKind.Auto)]
    private readonly struct NpmPartialVersion(int? major, int? minor, int? patch, bool hasWildcard, SemanticVersion lowerBound)
    {
        public int? Major { get; } = major;
        public int? Minor { get; } = minor;
        public int? Patch { get; } = patch;
        public bool HasWildcard { get; } = hasWildcard;

        /// <summary>The lowest version the partial version denotes, keeping any prerelease and metadata labels.</summary>
        public SemanticVersion LowerBound { get; } = lowerBound;
    }

    private static bool TryParseNpmPartialVersion(ReadOnlySpan<char> value, out NpmPartialVersion result)
    {
        result = default;

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

        // The prerelease and metadata labels are kept aside rather than discarded, so that the
        // lower bound of "^1.2.3-beta" is 1.2.3-beta and not 1.2.3.
        var core = GetVersionCore(value);
        var labels = value[core.Length..];

        // The component enumerator does not yield a trailing empty component, so "1.2." has to be
        // rejected here; an empty component anywhere else is caught in the loop below.
        if (core.IsEmpty || core[^1] is '.')
        {
            return false;
        }

        int? major = null;
        int? minor = null;
        int? patch = null;
        var hasWildcard = false;
        var partIndex = 0;

        foreach (var component in new PartEnumerable(core))
        {
            // A version has at most three components, and every one of them must be present.
            if (partIndex > 2 || component.IsEmpty)
            {
                return false;
            }

            if (component is "x" or "X" or "*")
            {
                hasWildcard = true;
            }
            else if (!TryParseInt(component, out var number))
            {
                return false;
            }
            else if (!hasWildcard)
            {
                // Everything after the first wildcard is a wildcard too: npm reads "1.x.2" as "1.x".
                switch (partIndex)
                {
                    case 0:
                        major = number;
                        break;
                    case 1:
                        minor = number;
                        break;
                    default:
                        patch = number;
                        break;
                }
            }

            partIndex++;
        }

        if (partIndex is 0)
        {
            return false;
        }

        SemanticVersion lowerBound;
        if (labels.IsEmpty)
        {
            lowerBound = new SemanticVersion(major ?? 0, minor ?? 0, patch ?? 0);
        }
        else if (patch is null || !SemanticVersion.TryParse(value, out lowerBound!))
        {
            // Labels only attach to a complete "major.minor.patch", and the whole thing has to be a
            // valid semantic version. Delegating that check keeps one definition of what is valid.
            return false;
        }

        result = new NpmPartialVersion(major, minor, patch, hasWildcard, lowerBound);
        return true;
    }

    private static bool TryParseInt(ReadOnlySpan<char> value, out int result)
    {
        return int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out result);
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
