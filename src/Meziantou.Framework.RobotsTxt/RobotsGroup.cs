namespace Meziantou.Framework.RobotsTxt;

/// <summary>
/// Represents a group of directives in a <c>robots.txt</c> file that applies to one or more user-agents.
/// </summary>
/// <remarks>
/// A group consists of one or more <c>User-agent</c> lines followed by <c>Allow</c>, <c>Disallow</c>,
/// and optional <c>Crawl-delay</c> directives.
/// </remarks>
public sealed class RobotsGroup
{
    internal RobotsGroup(IReadOnlyList<string> userAgents, IReadOnlyList<RobotsRule> rules, TimeSpan? crawlDelay)
    {
        UserAgents = userAgents;
        Rules = rules;
        CrawlDelay = crawlDelay;
    }

    /// <summary>Gets the user-agent tokens this group applies to (e.g. <c>"Googlebot"</c>, <c>"*"</c>).</summary>
    public IReadOnlyList<string> UserAgents { get; }

    /// <summary>Gets the ordered list of <c>Allow</c> and <c>Disallow</c> rules in this group.</summary>
    public IReadOnlyList<RobotsRule> Rules { get; }

    /// <summary>
    /// Gets the crawl delay specified by a <c>Crawl-delay</c> directive in this group,
    /// or <see langword="null"/> if none was specified.
    /// </summary>
    public TimeSpan? CrawlDelay { get; }

    /// <summary>
    /// Determines whether this group applies to the given <paramref name="userAgent"/>.
    /// </summary>
    /// <remarks>
    /// The comparison is case-insensitive. A group with user-agent <c>*</c> matches any agent.
    /// </remarks>
    public bool Matches(string userAgent)
    {
        ArgumentNullException.ThrowIfNull(userAgent);

        foreach (var agent in UserAgents)
        {
            if (agent == "*" || agent.Equals(userAgent, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Determines whether the given <paramref name="path"/> is allowed by the rules in this group.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The most specific (longest <see cref="RobotsRule.Value"/>) matching rule wins.
    /// When two matching rules have equal specificity, <see cref="RobotsRuleKind.Allow"/> takes precedence.
    /// </para>
    /// <para>
    /// An empty <c>Disallow</c> path is treated as "allow all" and only matches when no other rule matches.
    /// If no rule matches the path, access is allowed.
    /// </para>
    /// </remarks>
    /// <param name="path">The URL path (and optional query string) to check.</param>
    /// <returns><see langword="true"/> if access is allowed; <see langword="false"/> if it is disallowed.</returns>
    public bool IsAllowed(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return IsAllowed(path.AsSpan());
    }

    /// <summary>
    /// Determines whether the given <paramref name="path"/> is allowed by the rules in this group.
    /// </summary>
    public bool IsAllowed(ReadOnlySpan<char> path)
    {
        RobotsRule? bestMatch = null;
        int bestLength = -1;

        foreach (var rule in Rules)
        {
            // An empty Disallow is the special "allow everything" sentinel — skip it in matching.
            if (rule.Kind == RobotsRuleKind.Disallow && rule.Value.Length == 0)
                continue;

            if (!rule.Matches(path))
                continue;

            int len = rule.Value.Length;
            if (len > bestLength || (len == bestLength && rule.Kind == RobotsRuleKind.Allow))
            {
                bestMatch = rule;
                bestLength = len;
            }
        }

        // No matching rule → allowed. Empty Disallow → allowed.
        return bestMatch is null || bestMatch.Kind == RobotsRuleKind.Allow;
    }

    /// <inheritdoc/>
    public override string ToString() => $"User-agent: {string.Join(", ", UserAgents)}";
}
