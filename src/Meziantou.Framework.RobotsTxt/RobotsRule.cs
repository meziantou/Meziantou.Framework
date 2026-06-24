namespace Meziantou.Framework.RobotsTxt;

/// <summary>
/// Represents a single <c>Allow</c> or <c>Disallow</c> directive in a <c>robots.txt</c> file.
/// </summary>
/// <remarks>
/// Path patterns support Google's wildcard extensions:
/// <list type="bullet">
///   <item><description><c>*</c> matches any sequence of characters (zero or more).</description></item>
///   <item><description><c>$</c> at the end of the pattern anchors the match to the end of the path.</description></item>
/// </list>
/// </remarks>
public sealed class RobotsRule
{
    public RobotsRule(RobotsRuleKind kind, string value)
    {
        Kind = kind;
        Value = value;
    }

    /// <summary>Gets whether this rule allows or disallows access.</summary>
    public RobotsRuleKind Kind { get; }

    /// <summary>Gets the raw path pattern as it appears in the <c>robots.txt</c> file.</summary>
    public string Value { get; }

    /// <summary>
    /// Determines whether the given <paramref name="path"/> matches this rule's pattern,
    /// using Google's wildcard semantics (<c>*</c> and <c>$</c>).
    /// </summary>
    /// <param name="path">The URL path (and optional query string) to test.</param>
    /// <returns><see langword="true"/> if <paramref name="path"/> matches; otherwise <see langword="false"/>.</returns>
    public bool Matches(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return MatchesPattern(Value.AsSpan(), path.AsSpan());
    }

    /// <summary>
    /// Determines whether the given <paramref name="path"/> matches this rule's pattern,
    /// using Google's wildcard semantics (<c>*</c> and <c>$</c>).
    /// </summary>
    public bool Matches(ReadOnlySpan<char> path) => MatchesPattern(Value.AsSpan(), path);

    internal static bool MatchesPattern(ReadOnlySpan<char> pattern, ReadOnlySpan<char> path)
    {
        // An empty Disallow means "allow everything"; callers handle the semantic,
        // but an empty pattern still matches the empty prefix (all paths start with "").
        return WildcardMatch(pattern, path);
    }

    // Iterative wildcard matcher supporting '*' (any sequence) and '$' (end-anchor when last char).
    private static bool WildcardMatch(ReadOnlySpan<char> pattern, ReadOnlySpan<char> path)
    {
        // Strip trailing '$' and remember whether end-anchoring is required.
        var endAnchor = false;
        if (pattern.Length > 0 && pattern[^1] == '$')
        {
            endAnchor = true;
            pattern = pattern[..^1];
        }

        // Fast path: no wildcards.
        if (!pattern.Contains('*'))
        {
            if (endAnchor)
                return path.Equals(pattern, StringComparison.Ordinal);

            return path.StartsWith(pattern, StringComparison.Ordinal);
        }

        // dp[i] = true  →  pattern[0..i) matches some prefix of path.
        // We use two alternating arrays to stay O(pattern * path) in space.
        var pLen = pattern.Length;
        var sLen = path.Length;

        // patternIdx and pathIdx walk through segments split by '*'.
        var pi = 0; // index into pattern
        var si = 0; // index into path

        var starPatternIdx = -1;
        var starPathIdx = -1;

        while (si < sLen)
        {
            if (pi < pLen && pattern[pi] != '*')
            {
                if (pattern[pi] == path[si])
                {
                    pi++;
                    si++;
                }
                else if (starPatternIdx >= 0)
                {
                    // Backtrack: the '*' absorbs one more character.
                    starPathIdx++;
                    si = starPathIdx;
                    pi = starPatternIdx + 1;
                }
                else
                {
                    return false;
                }
            }
            else if (pi < pLen && pattern[pi] == '*')
            {
                starPatternIdx = pi;
                starPathIdx = si;
                pi++;
                // '*' initially matches zero characters.
            }
            else if (starPatternIdx >= 0)
            {
                starPathIdx++;
                si = starPathIdx;
                pi = starPatternIdx + 1;
            }
            else
            {
                return false;
            }

            // For prefix matching (no end-anchor), once the pattern is fully consumed
            // the match succeeds regardless of any remaining path characters.
            if (!endAnchor && pi == pLen)
                return true;
        }

        // Consume any trailing '*' in the pattern.
        while (pi < pLen && pattern[pi] == '*')
            pi++;

        var fullPatternConsumed = pi == pLen;

        if (!fullPatternConsumed)
            return false;

        // If end-anchored, the entire path must have been consumed.
        if (endAnchor)
            return si == sLen;

        // Otherwise a prefix match is sufficient (standard robots.txt semantics).
        return true;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Kind}: {Value}";
}
