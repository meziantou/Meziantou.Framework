using System.Text;

namespace Meziantou.Framework.RobotsTxt;

/// <summary>
/// Represents a parsed <c>robots.txt</c> file.
/// </summary>
/// <remarks>
/// <para>
/// The parser is lenient: unknown directives and malformed lines are silently ignored,
/// matching the behaviour of most web crawlers.
/// </para>
/// <para>
/// Supported directives: <c>User-agent</c>, <c>Allow</c>, <c>Disallow</c>,
/// <c>Crawl-delay</c>, and <c>Sitemap</c>.
/// </para>
/// <para>
/// Path patterns in <c>Allow</c> and <c>Disallow</c> directives support Google's wildcard
/// extensions: <c>*</c> matches any sequence of characters, and a trailing <c>$</c>
/// anchors the pattern to the end of the path.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var robots = RobotsFile.Parse("""
///     User-agent: *
///     Disallow: /private/
///     Allow: /public/
///
///     Sitemap: https://example.com/sitemap.xml
///     """);
///
/// bool allowed = robots.IsAllowed("Googlebot", "/public/page");
/// </code>
/// </example>
public sealed class RobotsFile
{
    private RobotsFile(IReadOnlyList<RobotsGroup> groups, IReadOnlyList<string> sitemaps)
    {
        Groups = groups;
        Sitemaps = sitemaps;
    }

    /// <summary>Gets the groups of directives defined in the file, in the order they appear.</summary>
    public IReadOnlyList<RobotsGroup> Groups { get; }

    /// <summary>
    /// Gets the sitemap URLs declared by <c>Sitemap:</c> directives, in the order they appear.
    /// </summary>
    public IReadOnlyList<string> Sitemaps { get; }

    /// <summary>Parses a <c>robots.txt</c> file from a string.</summary>
    /// <param name="content">The full text content of the <c>robots.txt</c> file.</param>
    public static RobotsFile Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Parse(content.AsSpan());
    }

    /// <summary>Parses a <c>robots.txt</c> file from a <see cref="ReadOnlySpan{T}"/> of characters.</summary>
    public static RobotsFile Parse(ReadOnlySpan<char> content)
    {
        var parser = new Parser();
        parser.Feed(content);
        return parser.Build();
    }

    /// <summary>
    /// Asynchronously parses a <c>robots.txt</c> file from a <see cref="Stream"/>.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="encoding">
    /// The text encoding to use. Defaults to <see cref="Encoding.UTF8"/> when <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static async Task<RobotsFile> ParseAsync(Stream stream, Encoding? encoding = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, encoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return await ParseAsync(reader, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Asynchronously parses a <c>robots.txt</c> file from a <see cref="TextReader"/>.</summary>
    /// <param name="reader">The text reader to read from.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static async Task<RobotsFile> ParseAsync(TextReader reader, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var parser = new Parser();
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            parser.FeedLine(line.AsSpan());
        }

        return parser.Build();
    }

    /// <summary>
    /// Returns the <see cref="RobotsGroup"/> that best matches the given <paramref name="userAgent"/>.
    /// </summary>
    /// <remarks>
    /// An exact (case-insensitive) match takes precedence over a catch-all (<c>*</c>) group.
    /// Returns <see langword="null"/> when no group matches.
    /// </remarks>
    public RobotsGroup? GetGroup(string userAgent)
    {
        ArgumentNullException.ThrowIfNull(userAgent);

        RobotsGroup? catchAll = null;
        foreach (var group in Groups)
        {
            foreach (var agent in group.UserAgents)
            {
                if (agent.Equals(userAgent, StringComparison.OrdinalIgnoreCase))
                    return group;

                if (agent == "*")
                    catchAll = group;
            }
        }

        return catchAll;
    }

    /// <summary>
    /// Determines whether the given <paramref name="userAgent"/> is allowed to access <paramref name="path"/>.
    /// </summary>
    /// <remarks>
    /// When no group matches the user-agent, access is allowed by default.
    /// </remarks>
    /// <param name="userAgent">The name of the crawling agent (e.g. <c>"Googlebot"</c>).</param>
    /// <param name="path">The URL path (and optional query string) to check.</param>
    public bool IsAllowed(string userAgent, string path)
    {
        ArgumentNullException.ThrowIfNull(userAgent);
        ArgumentNullException.ThrowIfNull(path);

        var group = GetGroup(userAgent);
        return group is null || group.IsAllowed(path);
    }

    /// <summary>
    /// Determines whether the given <paramref name="userAgent"/> is allowed to access the URL represented by <paramref name="uri"/>.
    /// </summary>
    /// <remarks>
    /// Only the path and query components of <paramref name="uri"/> are evaluated against the rules.
    /// When no group matches the user-agent, access is allowed by default.
    /// </remarks>
    public bool IsAllowed(string userAgent, Uri uri)
    {
        ArgumentNullException.ThrowIfNull(userAgent);
        ArgumentNullException.ThrowIfNull(uri);

        var pathAndQuery = uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString;
        return IsAllowed(userAgent, pathAndQuery);
    }

    /// <summary>
    /// Returns the crawl delay for the given <paramref name="userAgent"/>,
    /// or <see langword="null"/> when none is specified.
    /// </summary>
    public TimeSpan? GetCrawlDelay(string userAgent)
    {
        ArgumentNullException.ThrowIfNull(userAgent);
        return GetGroup(userAgent)?.CrawlDelay;
    }

    // -------------------------------------------------------------------------
    // Internal line-by-line parser
    // -------------------------------------------------------------------------

    private struct Parser
    {
        // State for the group currently being accumulated.
        private List<string>? _currentAgents;
        private List<RobotsRule>? _currentRules;
        private TimeSpan? _currentCrawlDelay;

        // Completed data.
        private List<RobotsGroup>? _groups;
        private List<string>? _sitemaps;

        public void Feed(ReadOnlySpan<char> content)
        {
            while (!content.IsEmpty)
            {
                int nl = content.IndexOfAny('\n', '\r');
                ReadOnlySpan<char> line;
                if (nl < 0)
                {
                    line = content;
                    content = [];
                }
                else
                {
                    line = content[..nl];
                    content = content[(nl + 1)..];
                    // Skip the '\n' after '\r'.
                    if (!content.IsEmpty && content[0] == '\n')
                        content = content[1..];
                }

                FeedLine(line);
            }
        }

        public void FeedLine(ReadOnlySpan<char> rawLine)
        {
            // Strip inline comments and trim whitespace.
            var line = StripComment(rawLine).Trim();

            if (line.IsEmpty)
            {
                FlushGroup();
                return;
            }

            // Split "directive: value"
            int colon = line.IndexOf(':');
            if (colon < 0)
                return; // malformed — ignore

            var directive = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();

            if (directive.Equals("User-agent", StringComparison.OrdinalIgnoreCase))
            {
                // A User-agent line after rules have started means a new group.
                if (_currentRules is { Count: > 0 })
                    FlushGroup();

                _currentAgents ??= [];
                _currentAgents.Add(value.ToString());
            }
            else if (directive.Equals("Allow", StringComparison.OrdinalIgnoreCase))
            {
                _currentRules ??= [];
                _currentRules.Add(new RobotsRule(RobotsRuleKind.Allow, value.ToString()));
            }
            else if (directive.Equals("Disallow", StringComparison.OrdinalIgnoreCase))
            {
                _currentRules ??= [];
                _currentRules.Add(new RobotsRule(RobotsRuleKind.Disallow, value.ToString()));
            }
            else if (directive.Equals("Crawl-delay", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
                    _currentCrawlDelay = TimeSpan.FromSeconds(seconds);
            }
            else if (directive.Equals("Sitemap", StringComparison.OrdinalIgnoreCase))
            {
                _sitemaps ??= [];
                _sitemaps.Add(value.ToString());
            }
            // Unknown directives are silently ignored (lenient mode).
        }

        public readonly RobotsFile Build()
        {
            // Flush any trailing group (file may not end with a blank line).
            var groups = _groups ?? [];
            if (_currentAgents is { Count: > 0 })
            {
                groups.Add(new RobotsGroup(
                    _currentAgents,
                    (IReadOnlyList<RobotsRule>?)_currentRules ?? [],
                    _currentCrawlDelay));
            }

            return new RobotsFile(groups, (IReadOnlyList<string>?)_sitemaps ?? []);
        }

        private void FlushGroup()
        {
            if (_currentAgents is null or { Count: 0 })
            {
                _currentAgents = null;
                _currentRules = null;
                _currentCrawlDelay = null;
                return;
            }

            _groups ??= [];
            _groups.Add(new RobotsGroup(
                _currentAgents,
                (IReadOnlyList<RobotsRule>?)_currentRules ?? [],
                _currentCrawlDelay));

            _currentAgents = null;
            _currentRules = null;
            _currentCrawlDelay = null;
        }

        private static ReadOnlySpan<char> StripComment(ReadOnlySpan<char> line)
        {
            int hash = line.IndexOf('#');
            return hash < 0 ? line : line[..hash];
        }
    }
}
