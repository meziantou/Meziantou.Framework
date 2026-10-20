using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.RobotsTxt.Fuzz;

/// <summary>
/// robots.txt files come from arbitrary sites, so parsing never throws: <see cref="RobotsFile.Parse(string)"/>, its span
/// overload and both <c>ParseAsync</c> overloads read the same file, which matches the one a straightforward reference
/// parser reads, and the reported errors point at lines of the input. Access checks never throw and agree with a reference
/// implementation of RFC 9309: the groups naming the agent (case-insensitively) are merged, or else the <c>*</c> groups,
/// the longest matching pattern wins, <c>Allow</c> wins a tie, and <c>*</c> and a trailing <c>$</c> are the only wildcards.
/// </summary>
public static partial class RobotsTxtFuzzTargets
{
    private const byte ContentSeparator = 0;
    private const int MaxQueries = 8;
    private const int MaxAgentsPerPath = 4;
    private const int MaxPathLength = 256;
    private const int MaxPatternLength = 1024;

    // Same bound as the parser: larger values do not fit in a TimeSpan once converted to ticks
    private const double MaxCrawlDelaySeconds = 922_337_203_685d;

    private const string DefaultQueries = "Bot\t/\nGooglebot\t/private/page\nA\t/x\nB\t/anything\n/index.php\n/public/page?q=1\n/fish.html\n/secret/public/page";

    private static readonly string[] Tokens =
    [
        "User-agent:", "user-agent:", "USER-AGENT: ", "Allow:", "allow:", "Disallow:", "disallow:", "Crawl-delay:", "crawl-delay:", "Sitemap:", "sitemap:", "Host:", "Clean-param:",
        "User-agent: *\n", "Disallow: /\n", "Allow: /\n", "Disallow:\n", "Crawl-delay: 10\n",
        "*", "$", "*$", "/*", "/*.php$", "/", "?", "&", "=", "%2F", "%7E", "~", ".", "#", ":", " ", "\t", "\n", "\r", "\r\n", "\n\n", "\uFEFF", "\u0000",
        "Googlebot", "googlebot", "Bingbot", "FuzzBot", "Googlebot-Image", "Googlebot/2.1",
        "0", "1", "1.5", "-1", "1e3", "1E20", "922337203685", "922337203686", "NaN", "Infinity", "-Infinity", ".5", "1,000",
        "https://example.com/sitemap.xml",
    ];

    private static readonly string[] RealWorldFiles =
    [
        // The example of RFC 9309, section 5.1
        "User-Agent: *\nDisallow: *.gif$\nDisallow: /example/\nAllow: /publications/\n\nUser-Agent: foobot\nDisallow:/\nAllow:/example/page.html\nAllow:/example/allowed.gif\n\nUser-Agent: barbot\nUser-Agent: bazbot\nDisallow: /example/page.html\n\nUser-Agent: quxbot\n\nEOF\n",

        // A CMS file: comments between the rules of a group
        "#\n# robots.txt\n#\nUser-agent: *\n# CSS, JS, Images\nAllow: /core/*.css$\nAllow: /core/*.css?\nAllow: /core/*.js$\nAllow: /core/*.js?\nAllow: /core/*.gif\n# Directories\nDisallow: /core/\nDisallow: /profiles/\n# Files\nDisallow: /README.md\n# Paths (clean URLs)\nDisallow: /admin/\nDisallow: /search/\nDisallow: /user/login\n# Paths (no clean URLs)\nDisallow: /index.php/admin/\nDisallow: /index.php/user/login\n",

        // CRLF line endings, a byte order mark, crawl delays, sitemaps and unknown directives
        "\uFEFFUser-agent: Googlebot\r\nUser-agent: Bingbot\r\nDisallow: /search\r\nAllow: /search/about\r\nCrawl-delay: 2.5\r\n\r\nUser-agent: *\r\nDisallow: /\r\nHost: www.example.com\r\nClean-param: ref /articles/\r\n\r\nSitemap: https://www.example.com/sitemap.xml\r\nSitemap: https://www.example.com/news-sitemap.xml\r\n",

        // Groups repeated for the same agent, and wildcards that tie
        "User-agent: googlebot\nDisallow: /a\n\nUser-agent: *\nDisallow: /\n\nUser-agent: Googlebot\nAllow: /a/b\nDisallow: /*.pdf$\nAllow: /a/*.pdf$\nCrawl-delay: 1\n\nUser-agent: GOOGLEBOT\nCrawl-delay: 9\n",

        // Malformed lines
        "User-agent *\nDisallow /private\nUser-agent: *\nDisallow: /private # comment\nCrawl-delay: 1,000\nCrawl-delay: NaN\nCrawl-delay: -1\nSitemap:\n:\n:value\nAllow:\n",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("robots-parse", Parse) { Seeds = GetParseSeeds, Dictionary = Tokens },
        new("robots-match", Match) { Seeds = GetMatchSeeds, Dictionary = Tokens },
        new("robots-rule-match", RuleMatch) { Seeds = GetRuleMatchSeeds, Dictionary = Tokens },
    ];

    /// <summary>
    /// The input is a robots.txt file. Every parse method reads the same file, it matches the reference parser, and the
    /// values and errors it holds come from the lines of the input.
    /// </summary>
    private static void Parse(ReadOnlySpan<byte> data)
    {
        var content = FuzzText.Decode(data);
        var robots = RobotsFile.Parse(content);
        var description = Describe(Snapshot(robots));

        FuzzAssert.Equal(description, Describe(Snapshot(RobotsFile.Parse(content.AsSpan()))), "Parse(ReadOnlySpan<char>) and Parse(string) disagree");

        var fromReader = ParseFromReaderAsync(content).GetAwaiter().GetResult();
        FuzzAssert.Equal(description, Describe(Snapshot(fromReader)), "ParseAsync(TextReader) and Parse(string) disagree");

        if (!HasUnpairedSurrogate(content))
        {
            var fromStream = ParseFromStreamAsync(Encoding.UTF8.GetBytes(content)).GetAwaiter().GetResult();

            // The StreamReader consumes the byte order mark of a UTF-8 stream, so the parser does not see it
            var expected = content.StartsWith('\uFEFF', StringComparison.Ordinal) ? Describe(Snapshot(RobotsFile.Parse(content[1..]))) : description;
            FuzzAssert.Equal(expected, Describe(Snapshot(fromStream)), "ParseAsync(Stream) and Parse(string) disagree");
        }

        FuzzAssert.Equal(Describe(ReferenceParse(content)), description, "The parser and the reference parser disagree");
        CheckFile(robots, content);
    }

    // The readers complete synchronously: they read from memory
    private static async Task<RobotsFile> ParseFromReaderAsync(string content)
    {
        using var reader = new StringReader(content);
        return await RobotsFile.ParseAsync(reader).ConfigureAwait(false);
    }

    private static async Task<RobotsFile> ParseFromStreamAsync(byte[] content)
    {
        using var stream = new MemoryStream(content);
        return await RobotsFile.ParseAsync(stream).ConfigureAwait(false);
    }

    /// <summary>
    /// The input is a robots.txt file, a NUL byte, then queries, one per line: <c>agent\tpath</c>, or a path alone that is
    /// checked for the first user agents of the file and an agent the file does not name. The selected group, the access
    /// and the crawl delay agree with the reference implementation.
    /// </summary>
    private static void Match(ReadOnlySpan<byte> data)
    {
        var content = FuzzText.Decode(FuzzInput.ReadUntil(ref data, ContentSeparator));
        var queries = ReadQueries(FuzzText.Decode(data));
        var robots = RobotsFile.Parse(content);

        var fileAgents = robots.Groups.SelectMany(group => group.UserAgents).Distinct(StringComparer.Ordinal).Take(MaxAgentsPerPath - 1).Append("FuzzBot").ToArray();
        foreach (var (agent, path) in queries)
        {
            foreach (var userAgent in agent is null ? fileAgents : [agent])
            {
                CheckQuery(robots, userAgent, path);
            }
        }
    }

    /// <summary>
    /// The input is a pattern, then paths, one per line. <see cref="RobotsRule.Matches(string)"/> agrees with a reference
    /// matcher, and a pattern always matches the path it gives once its wildcards are replaced by text.
    /// </summary>
    private static void RuleMatch(ReadOnlySpan<byte> data)
    {
        var lines = FuzzText.Decode(data).Split('\n');
        var pattern = Truncate(lines[0], MaxPatternLength);
        var rule = new RobotsRule(RobotsRuleKind.Disallow, pattern);
        _ = rule.ToString();

        foreach (var line in lines.Skip(1).Take(MaxQueries))
        {
            var path = Truncate(line, MaxPathLength);
            var expected = ReferenceMatches(pattern, path);
            FuzzAssert.Equal(expected, rule.Matches(path), $"Matches(string) and the reference matcher disagree on {FuzzAssert.Format(pattern)} and {FuzzAssert.Format(path)}");
            FuzzAssert.Equal(expected, rule.Matches(path.AsSpan()), $"Matches(ReadOnlySpan<char>) and the reference matcher disagree on {FuzzAssert.Format(pattern)} and {FuzzAssert.Format(path)}");
        }

        // '*' matches any text, the empty text included, and '$' only anchors the end
        var literal = pattern.EndsWith('$', StringComparison.Ordinal) ? pattern[..^1] : pattern;
        foreach (var replacement in (string[])["", "*", "x/y"])
        {
            var path = literal.Replace("*", replacement, StringComparison.Ordinal);
            FuzzAssert.True(ReferenceMatches(pattern, path), $"The reference matcher does not match {FuzzAssert.Format(pattern)} with {FuzzAssert.Format(path)}");
            FuzzAssert.True(rule.Matches(path), $"{FuzzAssert.Format(pattern)} does not match {FuzzAssert.Format(path)}, the pattern with its wildcards replaced by {FuzzAssert.Format(replacement)}");
            if (!pattern.EndsWith('$', StringComparison.Ordinal))
            {
                FuzzAssert.True(rule.Matches(path + "/z"), $"{FuzzAssert.Format(pattern)} matches {FuzzAssert.Format(path)} but not a path it prefixes");
            }
        }
    }

    private static void CheckQuery(RobotsFile robots, string userAgent, string path)
    {
        // RFC 9309 2.2.1: the groups naming the agent are merged; the '*' groups apply only when no group names it
        var selected = robots.Groups.Where(group => group.UserAgents.Any(agent => string.Equals(agent, userAgent, StringComparison.OrdinalIgnoreCase))).ToList();
        if (selected.Count is 0)
        {
            selected = [.. robots.Groups.Where(group => group.UserAgents.Contains("*", StringComparer.Ordinal))];
        }

        var group = robots.GetGroup(userAgent);
        var query = $"{FuzzAssert.Format(userAgent)} and {FuzzAssert.Format(path)}";
        if (selected.Count is 0)
        {
            FuzzAssert.True(group is null, $"GetGroup returns a group, but no group applies to {FuzzAssert.Format(userAgent)}");
            FuzzAssert.True(robots.IsAllowed(userAgent, path), $"A path is disallowed although no group applies: {query}");
            FuzzAssert.Equal(null, robots.GetCrawlDelay(userAgent), $"A crawl delay is returned although no group applies to {FuzzAssert.Format(userAgent)}");
            return;
        }

        FuzzAssert.True(group is not null, $"GetGroup returns null, but {selected.Count} groups apply to {FuzzAssert.Format(userAgent)}");
        if (selected.Count is 1)
        {
            FuzzAssert.True(ReferenceEquals(selected[0], group), $"GetGroup does not return the only group that applies to {FuzzAssert.Format(userAgent)}");
        }

        FuzzAssert.True(group.Matches(userAgent), $"The group returned by GetGroup does not match {FuzzAssert.Format(userAgent)}");
        _ = group.ToString();

        // A merged group lists the union of the declared tokens; a single group is returned as is
        if (selected.Count > 1)
        {
            var expectedAgents = new List<string>();
            foreach (var agent in selected.SelectMany(selectedGroup => selectedGroup.UserAgents))
            {
                if (!expectedAgents.Contains(agent, StringComparer.OrdinalIgnoreCase))
                {
                    expectedAgents.Add(agent);
                }
            }

            FuzzAssert.Equal(Describe(expectedAgents), Describe(group.UserAgents), $"The merged group of {FuzzAssert.Format(userAgent)} has unexpected user agents");
        }

        var expectedRules = selected.SelectMany(selectedGroup => selectedGroup.Rules).ToList();
        FuzzAssert.True(expectedRules.SequenceEqual(group.Rules), $"The merged group of {FuzzAssert.Format(userAgent)} does not hold the rules of the groups that apply, in file order");

        var expectedCrawlDelay = selected.Select(selectedGroup => selectedGroup.CrawlDelay).FirstOrDefault(delay => delay is not null);
        FuzzAssert.Equal(expectedCrawlDelay, group.CrawlDelay, $"The merged group of {FuzzAssert.Format(userAgent)} does not hold the first crawl delay");
        FuzzAssert.Equal(expectedCrawlDelay, robots.GetCrawlDelay(userAgent), $"GetCrawlDelay is not the crawl delay of the group of {FuzzAssert.Format(userAgent)}");

        var expected = ReferenceIsAllowed(expectedRules, path);
        FuzzAssert.Equal(expected, robots.IsAllowed(userAgent, path), $"IsAllowed and the reference disagree on {query}");
        FuzzAssert.Equal(expected, group.IsAllowed(path), $"RobotsGroup.IsAllowed(string) and the reference disagree on {query}");
        FuzzAssert.Equal(expected, group.IsAllowed(path.AsSpan()), $"RobotsGroup.IsAllowed(ReadOnlySpan<char>) and the reference disagree on {query}");

        if (Uri.TryCreate(path, UriKind.Relative, out var relativeUri))
        {
            FuzzAssert.Equal(expected, robots.IsAllowed(userAgent, relativeUri), $"IsAllowed(string, Uri) and IsAllowed(string, string) disagree on a relative URI: {query}");
        }

        if (Uri.TryCreate("https://example.com" + path, UriKind.Absolute, out var absoluteUri))
        {
            FuzzAssert.Equal(robots.IsAllowed(userAgent, absoluteUri.PathAndQuery), robots.IsAllowed(userAgent, absoluteUri), $"IsAllowed(string, Uri) does not check the path and query of an absolute URI: {query}");
        }
    }

    private static void CheckFile(RobotsFile robots, string content)
    {
        var lines = SplitLines(content);
        var previousLineNumber = 0;
        foreach (var error in robots.ParseErrors)
        {
            FuzzAssert.True(error.LineNumber > previousLineNumber && error.LineNumber <= lines.Count, $"The error {FuzzAssert.Format(error.ToString())} is not on a line of the input after the previous error (the input has {lines.Count} lines)");
            FuzzAssert.Equal(lines[error.LineNumber - 1], error.Line, $"The error at line {error.LineNumber} does not hold the line");
            FuzzAssert.True(Enum.IsDefined(error.Kind), $"Unknown error kind {error.Kind}");
            previousLineNumber = error.LineNumber;
        }

        foreach (var group in robots.Groups)
        {
            FuzzAssert.True(group.UserAgents.Count > 0, "A group has no user agent");
            foreach (var agent in group.UserAgents)
            {
                CheckValue(agent, "user agent");
            }

            foreach (var rule in group.Rules)
            {
                CheckValue(rule.Value, "rule value");
                FuzzAssert.True(rule.Kind is RobotsRuleKind.Allow or RobotsRuleKind.Disallow, $"Unknown rule kind {rule.Kind}");
                _ = rule.ToString();
            }

            FuzzAssert.True(group.CrawlDelay is null || group.CrawlDelay >= TimeSpan.Zero, $"A crawl delay is negative: {group.CrawlDelay}");
        }

        foreach (var sitemap in robots.Sitemaps)
        {
            CheckValue(sitemap, "sitemap");
        }

        void CheckValue(string value, string description)
        {
            FuzzAssert.False(value.AsSpan().ContainsAny('\r', '\n', '#'), $"A {description} holds a line break or a comment: {FuzzAssert.Format(value)}");
            FuzzAssert.Equal(value.Trim(), value, $"A {description} is not trimmed");
            FuzzAssert.True(content.Contains(value, StringComparison.Ordinal), $"A {description} is not text of the input: {FuzzAssert.Format(value)}");
        }
    }

    /// <summary>Parses a robots.txt file the way the documentation describes it, written independently of the parser.</summary>
    private static ParsedFile ReferenceParse(string content)
    {
        var file = new ParsedFile([], [], []);
        List<string>? agents = null;
        var rules = new List<ParsedRule>();
        TimeSpan? crawlDelay = null;
        var hasBody = false;

        var lines = SplitLines(content);
        for (var i = 0; i < lines.Count; i++)
        {
            var lineNumber = i + 1;
            var line = lines[i];

            // A byte order mark can only start the file
            if (lineNumber is 1 && line.StartsWith('\uFEFF', StringComparison.Ordinal))
            {
                line = line[1..];
            }

            var commentIndex = line.IndexOf('#', StringComparison.Ordinal);
            var text = (commentIndex < 0 ? line : line[..commentIndex]).Trim();
            if (text.Length is 0)
            {
                // A blank line ends the group; a line that only holds a comment is ignored
                if (commentIndex < 0)
                {
                    EndGroup();
                }

                continue;
            }

            var colonIndex = text.IndexOf(':', StringComparison.Ordinal);
            if (colonIndex < 0)
            {
                file.Errors.Add(new(lineNumber, lines[i], RobotsParseErrorKind.MalformedLine));
                continue;
            }

            var name = text[..colonIndex].Trim();
            var value = text[(colonIndex + 1)..].Trim();
            if (IsDirective(name, "user-agent"))
            {
                // The User-agent lines that follow the body of a group start a new group
                if (hasBody)
                {
                    EndGroup();
                }

                agents ??= [];
                agents.Add(value);
            }
            else if (IsDirective(name, "allow") || IsDirective(name, "disallow"))
            {
                hasBody = true;
                rules.Add(new(IsDirective(name, "allow") ? RobotsRuleKind.Allow : RobotsRuleKind.Disallow, value));
            }
            else if (IsDirective(name, "crawl-delay"))
            {
                hasBody = true;
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds is >= 0 and <= MaxCrawlDelaySeconds)
                {
                    crawlDelay = TimeSpan.FromSeconds(seconds);
                }
                else
                {
                    file.Errors.Add(new(lineNumber, lines[i], RobotsParseErrorKind.InvalidCrawlDelay));
                }
            }
            else if (IsDirective(name, "sitemap"))
            {
                file.Sitemaps.Add(value);
            }
            else
            {
                file.Errors.Add(new(lineNumber, lines[i], RobotsParseErrorKind.UnknownDirective));
            }
        }

        EndGroup();
        return file;

        void EndGroup()
        {
            // The rules that precede the first User-agent line of a group belong to no group
            if (agents is not null)
            {
                file.Groups.Add(new(agents, rules, crawlDelay));
            }

            agents = null;
            rules = [];
            crawlDelay = null;
            hasBody = false;
        }
    }

    /// <summary>Compares a directive name to a lowercase ASCII name, ignoring the case of the ASCII letters only.</summary>
    private static bool IsDirective(string name, string expected)
    {
        if (name.Length != expected.Length)
            return false;

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i] is >= 'A' and <= 'Z' ? (char)(name[i] + ('a' - 'A')) : name[i];
            if (c != expected[i])
                return false;
        }

        return true;
    }

    /// <summary>Splits the text at CR, LF and CRLF (RFC 9309); a final line break does not start an empty line.</summary>
    private static List<string> SplitLines(string content)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] is not ('\r' or '\n'))
                continue;

            lines.Add(content[start..i]);
            if (content[i] is '\r' && i + 1 < content.Length && content[i + 1] is '\n')
            {
                i++;
            }

            start = i + 1;
        }

        if (start < content.Length)
        {
            lines.Add(content[start..]);
        }

        return lines;
    }

    private static bool ReferenceIsAllowed(List<RobotsRule> rules, string path)
    {
        var bestLength = -1;
        var isAllowed = true;
        foreach (var rule in rules)
        {
            // An empty Disallow disallows nothing
            if (rule.Kind is RobotsRuleKind.Disallow && rule.Value.Length is 0)
                continue;

            if (!ReferenceMatches(rule.Value, path))
                continue;

            // The longest pattern wins, and Allow wins a tie
            if (rule.Value.Length > bestLength)
            {
                bestLength = rule.Value.Length;
                isAllowed = rule.Kind is RobotsRuleKind.Allow;
            }
            else if (rule.Value.Length == bestLength && rule.Kind is RobotsRuleKind.Allow)
            {
                isAllowed = true;
            }
        }

        return isAllowed;
    }

    /// <summary>
    /// Matches a pattern against a prefix of the path (or the whole path when the pattern ends with <c>$</c>) by tracking
    /// every length of path that the pattern read so far can match.
    /// </summary>
    private static bool ReferenceMatches(string pattern, string path)
    {
        var isAnchored = pattern.EndsWith('$', StringComparison.Ordinal);
        if (isAnchored)
        {
            pattern = pattern[..^1];
        }

        var matched = new bool[path.Length + 1];
        matched[0] = true;
        foreach (var c in pattern)
        {
            var next = new bool[path.Length + 1];
            if (c is '*')
            {
                var any = false;
                for (var length = 0; length <= path.Length; length++)
                {
                    any |= matched[length];
                    next[length] = any;
                }
            }
            else
            {
                for (var length = 0; length < path.Length; length++)
                {
                    next[length + 1] = matched[length] && path[length] == c;
                }
            }

            matched = next;
        }

        return isAnchored ? matched[path.Length] : matched.Contains(true);
    }

    private static List<(string? Agent, string Path)> ReadQueries(string text)
    {
        var queries = new List<(string? Agent, string Path)>();
        foreach (var line in text.Split('\n').Take(MaxQueries))
        {
            var tabIndex = line.IndexOf('\t', StringComparison.Ordinal);
            queries.Add(tabIndex < 0 ? (null, Truncate(line, MaxPathLength)) : (line[..tabIndex], Truncate(line[(tabIndex + 1)..], MaxPathLength)));
        }

        return queries;
    }

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    private static bool HasUnpairedSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static ParsedFile Snapshot(RobotsFile robots)
    {
        return new ParsedFile(
            [.. robots.Groups.Select(group => new ParsedGroup([.. group.UserAgents], [.. group.Rules.Select(rule => new ParsedRule(rule.Kind, rule.Value))], group.CrawlDelay))],
            [.. robots.Sitemaps],
            [.. robots.ParseErrors.Select(error => new ParsedError(error.LineNumber, error.Line, error.Kind))]);
    }

    private static string Describe(ParsedFile file)
    {
        var sb = new StringBuilder();
        foreach (var group in file.Groups)
        {
            sb.Append("group ").Append(Describe(group.Agents));
            foreach (var rule in group.Rules)
            {
                sb.Append(CultureInfo.InvariantCulture, $" {rule.Kind} {FuzzAssert.Format(rule.Value)}");
            }

            if (group.CrawlDelay is { } crawlDelay)
            {
                sb.Append(CultureInfo.InvariantCulture, $" crawl-delay {crawlDelay.Ticks}");
            }

            sb.Append('\n');
        }

        foreach (var sitemap in file.Sitemaps)
        {
            sb.Append("sitemap ").Append(FuzzAssert.Format(sitemap)).Append('\n');
        }

        foreach (var error in file.Errors)
        {
            sb.Append(CultureInfo.InvariantCulture, $"error {error.LineNumber} {error.Kind} {FuzzAssert.Format(error.Line)}\n");
        }

        return sb.ToString();
    }

    private static string Describe(IEnumerable<string> values) => "[" + string.Join(", ", values.Select(FuzzAssert.Format)) + "]";

    private static IEnumerable<FuzzSeed> GetParseSeeds()
    {
        var index = 0;
        foreach (var content in GetFiles())
        {
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"file-{index++}"), content);
        }
    }

    private static IEnumerable<FuzzSeed> GetMatchSeeds()
    {
        var index = 0;
        foreach (var content in GetFiles())
        {
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"file-{index++}"), content + '\0' + DefaultQueries);
        }

        var queries = new List<string>();
        foreach (Match match in IsAllowedRegex().Matches(ReadTests()))
        {
            queries.Add(Regex.Unescape(match.Groups["agent"].Value) + '\t' + Regex.Unescape(match.Groups["path"].Value));
        }

        foreach (var chunk in queries.Distinct(StringComparer.Ordinal).Chunk(MaxQueries))
        {
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"tests-{index++}"), RealWorldFiles[0] + '\0' + string.Join('\n', chunk));
        }
    }

    private static IEnumerable<FuzzSeed> GetRuleMatchSeeds()
    {
        var index = 0;
        foreach (var group in RuleRegex().Matches(ReadTests()).GroupBy(match => match.Groups["pattern"].Value, StringComparer.Ordinal))
        {
            var lines = group.Select(match => Regex.Unescape(match.Groups["path"].Value)).Prepend(Regex.Unescape(group.Key));
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"test-{index++}"), string.Join('\n', lines));
        }

        yield return FuzzSeed.FromText("anchored", "/*.php$\n/index.php\n/index.php?x=1\n/a/b.php\n.php");
        yield return FuzzSeed.FromText("stars", "/a*b*c\n/abc\n/aXbYc\n/acb\n/a**b**c/d");
        yield return FuzzSeed.FromText("dollar-inside", "/a$b\n/a$b\n/a$bc\n/ab");
        yield return FuzzSeed.FromText("only-dollar", "$\n\n/");
        yield return FuzzSeed.FromText("backtracking", "/*aa*ab$\n/aaaaaaaaaaaaaaaaab\n/aaaaaaaaaaaaaaaaaa\n/ab");
    }

    /// <summary>Gets the robots.txt files of the unit tests and real-world files.</summary>
    private static IEnumerable<string> GetFiles()
    {
        var source = ReadTests();
        foreach (Match match in RawStringRegex().Matches(source))
        {
            var indentation = match.Groups["indent"].Value;
            var lines = match.Groups["body"].Value.ReplaceLineEndings("\n").Split('\n');
            yield return string.Join('\n', lines.Select(line => line.StartsWith(indentation, StringComparison.Ordinal) ? line[indentation.Length..] : line.TrimStart()));
        }

        foreach (Match match in StringLiteralRegex().Matches(source))
        {
            yield return Regex.Unescape(match.Groups["value"].Value);
        }

        foreach (var file in RealWorldFiles)
        {
            yield return file;
        }
    }

    private static string ReadTests() => FuzzResources.ReadText(typeof(RobotsTxtFuzzTargets).Assembly, "RobotsFileTests.cs");

    private sealed record ParsedFile(List<ParsedGroup> Groups, List<string> Sitemaps, List<ParsedError> Errors);

    private sealed record ParsedGroup(List<string> Agents, List<ParsedRule> Rules, TimeSpan? CrawlDelay);

    private sealed record ParsedRule(RobotsRuleKind Kind, string Value);

    private sealed record ParsedError(int LineNumber, string Line, RobotsParseErrorKind Kind);

    [GeneratedRegex("""Parse\(\s*(?<!\$)\"{3}[ \t]*\r?\n(?<body>[\s\S]*?)\r?\n(?<indent>[ \t]*)\"{3}(?!\")""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex RawStringRegex();

    [GeneratedRegex("""Parse\("(?<value>(?:[^"\\]|\\.)*)"\)""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex StringLiteralRegex();

    [GeneratedRegex("""IsAllowed\("(?<agent>(?:[^"\\]|\\.)*)", "(?<path>(?:[^"\\]|\\.)*)"\)""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex IsAllowedRegex();

    [GeneratedRegex("""InlineData\("(?<pattern>(?:[^"\\]|\\.)*)", "(?<path>(?:[^"\\]|\\.)*)", (?:true|false)\)""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex RuleRegex();
}
