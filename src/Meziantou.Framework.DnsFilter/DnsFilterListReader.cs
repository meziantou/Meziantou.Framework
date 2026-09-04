using System.Net;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.DnsFilter;

/// <summary>
/// Reads DNS filter lists in various formats and produces <see cref="DnsFilterRule"/> instances.
/// Supports hosts files, domains-only lists, and AdGuard/Adblock DNS filtering syntax.
/// </summary>
/// <remarks>
/// Parsing is strict: a rule carrying a modifier this library does not implement, or a modifier
/// whose value cannot be parsed, is discarded rather than applied without it. Ignoring an
/// unrecognized modifier would silently widen the rule — turning <c>@@||x^$script</c> into a
/// blanket allow, or <c>||x^$dnstype=SVCB</c> into a block of every record type. Use
/// <see cref="ParseWithDiagnostics(string, DnsFilterListFormat)"/> to see what was discarded.
/// </remarks>
public static class DnsFilterListReader
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly string[] LocalhostNames =
    [
        "localhost", "localhost.localdomain", "local", "broadcasthost",
        "ip6-localhost", "ip6-loopback", "ip6-localnet", "ip6-mcastprefix",
        "ip6-allnodes", "ip6-allrouters", "ip6-allhosts",
    ];

    /// <summary>
    /// Parses all rules from the specified <see cref="TextReader"/>.
    /// </summary>
    /// <param name="reader">The text reader containing the filter list.</param>
    /// <param name="format">The format of the filter list. Defaults to <see cref="DnsFilterListFormat.AutoDetect"/>.</param>
    /// <returns>A list of parsed filter rules. Lines that could not be parsed are skipped silently;
    /// use <see cref="ParseWithDiagnostics(TextReader, DnsFilterListFormat)"/> to observe them.</returns>
    public static IReadOnlyList<DnsFilterRule> Parse(TextReader reader, DnsFilterListFormat format = DnsFilterListFormat.AutoDetect)
        => ParseWithDiagnostics(reader, format).Rules;

    /// <summary>
    /// Parses all rules from the specified string.
    /// </summary>
    /// <param name="text">The filter list text.</param>
    /// <param name="format">The format of the filter list. Defaults to <see cref="DnsFilterListFormat.AutoDetect"/>.</param>
    /// <returns>A list of parsed filter rules. Lines that could not be parsed are skipped silently;
    /// use <see cref="ParseWithDiagnostics(string, DnsFilterListFormat)"/> to observe them.</returns>
    public static IReadOnlyList<DnsFilterRule> Parse(string text, DnsFilterListFormat format = DnsFilterListFormat.AutoDetect)
        => ParseWithDiagnostics(text, format).Rules;

    /// <summary>
    /// Parses a filter list, reporting every line that did not produce a rule.
    /// </summary>
    /// <param name="reader">The text reader containing the filter list.</param>
    /// <param name="format">The format of the filter list.</param>
    public static DnsFilterParseResult ParseWithDiagnostics(TextReader reader, DnsFilterListFormat format = DnsFilterListFormat.AutoDetect)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var lines = ReadAllLines(reader);
        if (lines.Count is 0)
            return new DnsFilterParseResult([], []) { Format = format is DnsFilterListFormat.AutoDetect ? DnsFilterListFormat.DomainsOnly : format };

        if (format is DnsFilterListFormat.AutoDetect)
        {
            format = DetectFormat(lines);
        }

        var diagnostics = new List<DnsFilterParseDiagnostic>();
        var rules = format switch
        {
            DnsFilterListFormat.Hosts => ParseHostsFormat(lines, diagnostics),
            DnsFilterListFormat.DomainsOnly => ParseDomainsOnlyFormat(lines, diagnostics),
            DnsFilterListFormat.AdBlock => ParseAdBlockFormat(lines, diagnostics),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown filter list format."),
        };

        return new DnsFilterParseResult(rules, diagnostics) { Format = format };
    }

    /// <summary>
    /// Parses a filter list, reporting every line that did not produce a rule.
    /// </summary>
    /// <param name="text">The filter list text.</param>
    /// <param name="format">The format of the filter list.</param>
    public static DnsFilterParseResult ParseWithDiagnostics(string text, DnsFilterListFormat format = DnsFilterListFormat.AutoDetect)
    {
        ArgumentNullException.ThrowIfNull(text);
        using var reader = new StringReader(text);
        return ParseWithDiagnostics(reader, format);
    }

    private static List<string> ReadAllLines(TextReader reader)
    {
        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lines.Add(line);
        }

        return lines;
    }

    /// <summary>
    /// Classifies a line for format detection and for the per-format parsers, so that "what is a
    /// comment" is defined exactly once.
    /// </summary>
    private static LineKind Classify(string line, out string content)
    {
        content = "";

        var trimmed = line.Trim();
        if (trimmed.Length is 0)
            return LineKind.Empty;

        // Adblock list header, e.g. "[Adblock Plus 2.0]"
        if (trimmed.StartsWith('[', StringComparison.Ordinal))
            return LineKind.AdBlockComment;

        // '!' introduces an Adblock comment; '#' a hosts/domains comment. A '##' or '#?#' line is a
        // cosmetic Adblock rule, which is meaningless for DNS either way.
        if (trimmed.StartsWith('!', StringComparison.Ordinal))
            return LineKind.AdBlockComment;

        if (trimmed.StartsWith('#', StringComparison.Ordinal))
            return LineKind.Comment;

        // Strip an inline comment before looking at the content. Doing this first is what keeps a
        // '$' inside a trailing "# costs $5" from being mistaken for a modifier separator.
        var commentIndex = trimmed.IndexOf('#', StringComparison.Ordinal);
        if (commentIndex >= 0)
        {
            trimmed = trimmed[..commentIndex].TrimEnd();
            if (trimmed.Length is 0)
                return LineKind.Comment;
        }

        content = trimmed;

        if (trimmed.StartsWith("||", StringComparison.Ordinal) ||
            trimmed.StartsWith("@@", StringComparison.Ordinal) ||
            trimmed.StartsWith('/', StringComparison.Ordinal) ||
            trimmed.StartsWith('|', StringComparison.Ordinal) ||
            trimmed.Contains('*', StringComparison.Ordinal) ||
            trimmed.Contains('^', StringComparison.Ordinal) ||
            trimmed.Contains('$', StringComparison.Ordinal))
        {
            return LineKind.AdBlockRule;
        }

        return IsHostsEntry(trimmed) ? LineKind.HostsEntry : LineKind.Domain;
    }

    internal static DnsFilterListFormat DetectFormat(IReadOnlyList<string> lines)
    {
        // Count the evidence rather than latching onto the first marker seen: a single odd line in
        // a 150k-entry hosts file must not reclassify the whole list, because every hosts line
        // would then be parsed as an Adblock pattern and silently match nothing.
        var adBlockRules = 0;
        var hostsEntries = 0;
        var domains = 0;
        var adBlockComments = 0;

        foreach (var line in lines)
        {
            switch (Classify(line, out _))
            {
                case LineKind.AdBlockRule: adBlockRules++; break;
                case LineKind.HostsEntry: hostsEntries++; break;
                case LineKind.Domain: domains++; break;
                case LineKind.AdBlockComment: adBlockComments++; break;
                default: break;
            }
        }

        if (adBlockRules > 0 && adBlockRules >= hostsEntries)
            return DnsFilterListFormat.AdBlock;

        if (hostsEntries > 0)
            return DnsFilterListFormat.Hosts;

        if (adBlockRules > 0)
            return DnsFilterListFormat.AdBlock;

        // A list of bare domains introduced by '!' headers is an Adblock-flavoured list, but every
        // rule in it is a plain domain, so either parser produces the same rules.
        return domains > 0 || adBlockComments is 0 ? DnsFilterListFormat.DomainsOnly : DnsFilterListFormat.AdBlock;
    }

    private static bool IsHostsEntry(string line)
    {
        var spaceIndex = line.IndexOfAny([' ', '\t']);
        if (spaceIndex <= 0)
            return false;

        return IPAddress.TryParse(line.AsSpan(0, spaceIndex), out _);
    }

    private static List<DnsFilterRule> ParseHostsFormat(List<string> lines, List<DnsFilterParseDiagnostic> diagnostics)
    {
        var rules = new List<DnsFilterRule>();

        for (var i = 0; i < lines.Count; i++)
        {
            var kind = Classify(lines[i], out var content);
            if (kind is LineKind.Empty or LineKind.Comment or LineKind.AdBlockComment)
                continue;

            var parts = content.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !IPAddress.TryParse(parts[0], out _))
            {
                diagnostics.Add(new DnsFilterParseDiagnostic(i + 1, lines[i], DnsFilterParseError.InvalidPattern, "not a hosts entry"));
                continue;
            }

            for (var j = 1; j < parts.Length; j++)
            {
                if (!DnsDomainName.TryNormalize(parts[j], out var domain))
                {
                    diagnostics.Add(new DnsFilterParseDiagnostic(i + 1, lines[i], DnsFilterParseError.InvalidPattern, parts[j]));
                    continue;
                }

                if (Array.IndexOf(LocalhostNames, domain) >= 0)
                    continue;

                rules.Add(new DnsFilterRule
                {
                    OriginalText = content,
                    Action = DnsFilterAction.Block,
                    ExactDomain = domain,
                    BadFilterKey = "e:" + domain,
                });
            }
        }

        return rules;
    }

    private static List<DnsFilterRule> ParseDomainsOnlyFormat(List<string> lines, List<DnsFilterParseDiagnostic> diagnostics)
    {
        var rules = new List<DnsFilterRule>();

        for (var i = 0; i < lines.Count; i++)
        {
            var kind = Classify(lines[i], out var content);
            if (kind is LineKind.Empty or LineKind.Comment or LineKind.AdBlockComment)
                continue;

            if (!DnsDomainName.TryNormalize(content, out var domain))
            {
                diagnostics.Add(new DnsFilterParseDiagnostic(i + 1, lines[i], DnsFilterParseError.InvalidPattern, content));
                continue;
            }

            rules.Add(new DnsFilterRule
            {
                OriginalText = content,
                Action = DnsFilterAction.Block,
                ExactDomain = domain,
                BadFilterKey = "e:" + domain,
            });
        }

        return rules;
    }

    private static List<DnsFilterRule> ParseAdBlockFormat(List<string> lines, List<DnsFilterParseDiagnostic> diagnostics)
    {
        var rules = new List<DnsFilterRule>();

        for (var i = 0; i < lines.Count; i++)
        {
            var kind = Classify(lines[i], out var content);
            if (kind is LineKind.Empty or LineKind.Comment or LineKind.AdBlockComment)
                continue;

            if (TryParseAdBlockRule(content, out var rule, out var error, out var detail))
            {
                rules.Add(rule);
            }
            else
            {
                diagnostics.Add(new DnsFilterParseDiagnostic(i + 1, lines[i], error, detail));
            }
        }

        return rules;
    }

    internal static bool TryParseAdBlockRule(string text, [NotNullWhen(true)] out DnsFilterRule? rule)
        => TryParseAdBlockRule(text, out rule, out _, out _);

    private static bool TryParseAdBlockRule(
        string text,
        [NotNullWhen(true)] out DnsFilterRule? rule,
        out DnsFilterParseError error,
        out string? detail)
    {
        rule = null;
        error = DnsFilterParseError.InvalidPattern;
        detail = null;

        var action = DnsFilterAction.Block;
        var remaining = text.AsSpan();

        if (remaining.StartsWith("@@", StringComparison.Ordinal))
        {
            action = DnsFilterAction.Allow;
            remaining = remaining[2..];
        }

        string? modifiersPart = null;
        string patternPart;

        if (remaining.StartsWith("/", StringComparison.Ordinal))
        {
            // A '/regex/' rule. The closing delimiter must be found without being fooled by an
            // escaped slash inside the expression.
            var closingSlash = FindRegexEnd(remaining);
            if (closingSlash < 0)
            {
                error = DnsFilterParseError.InvalidRegex;
                detail = "missing closing '/'";
                return false;
            }

            var afterRegex = remaining[(closingSlash + 1)..];
            if (afterRegex.StartsWith("$", StringComparison.Ordinal))
            {
                modifiersPart = afterRegex[1..].ToString();
            }
            else if (!afterRegex.IsEmpty)
            {
                error = DnsFilterParseError.InvalidRegex;
                detail = "unexpected text after closing '/'";
                return false;
            }

            patternPart = remaining[1..closingSlash].ToString();
            return TryBuildRegexRule(text, patternPart, action, modifiersPart, GetRegexLiteralPrefix(patternPart) is { } prefix ? [prefix] : null, patternSuffix: null, out rule, out error, out detail);
        }

        var dollarIndex = remaining.IndexOf('$');
        if (dollarIndex >= 0)
        {
            modifiersPart = remaining[(dollarIndex + 1)..].ToString();
            remaining = remaining[..dollarIndex];
        }

        patternPart = remaining.ToString();
        return TryBuildPatternRule(text, patternPart, action, modifiersPart, out rule, out error, out detail);
    }

    /// <summary>
    /// Finds the index of the closing '/' of a regex rule, honouring backslash escapes.
    /// </summary>
    private static int FindRegexEnd(ReadOnlySpan<char> value)
    {
        for (var i = 1; i < value.Length; i++)
        {
            if (value[i] is '\\')
            {
                i++;
                continue;
            }

            if (value[i] is '/')
                return i;
        }

        return -1;
    }

    private static bool TryBuildPatternRule(
        string text,
        string patternPart,
        DnsFilterAction action,
        string? modifiersPart,
        [NotNullWhen(true)] out DnsFilterRule? rule,
        out DnsFilterParseError error,
        out string? detail)
    {
        rule = null;
        error = DnsFilterParseError.InvalidPattern;
        detail = null;

        // Strip anchors into flags first. Dispatching on them as mutually exclusive branches is what
        // used to make '||*.example.com^' fall into the domain branch and be stored as a literal.
        var pattern = patternPart.AsSpan();
        var matchSubdomains = false;

        if (pattern.StartsWith("||", StringComparison.Ordinal))
        {
            matchSubdomains = true;
            pattern = pattern[2..];
        }
        else if (pattern.StartsWith("|", StringComparison.Ordinal))
        {
            // A leading '|' anchors to the start of the name, which for DNS is implicit.
            pattern = pattern[1..];
        }

        if (pattern.EndsWith("|", StringComparison.Ordinal))
        {
            pattern = pattern[..^1];
        }

        // '^' is a separator/terminator in Adblock syntax; for DNS it only ever means end-of-name.
        pattern = pattern.TrimEnd('^');

        if (pattern.IsEmpty)
        {
            detail = "empty pattern";
            return false;
        }

        var patternText = pattern.ToString();

        if (patternText.Contains('*', StringComparison.Ordinal))
        {
            var regexPattern = BuildWildcardRegex(patternText, matchSubdomains);
            return TryBuildRegexRule(text, regexPattern, action, modifiersPart, GetWildcardLiterals(patternText), GetPatternSuffix(patternText), out rule, out error, out detail);
        }

        if (!DnsDomainName.TryNormalize(patternText, out var domain))
        {
            detail = patternText;
            return false;
        }

        if (!TryParseModifiers(modifiersPart, out var modifiers, out error, out detail))
            return false;

        var key = (action is DnsFilterAction.Allow ? "@@" : "") + (matchSubdomains ? "s:" : "e:") + domain;

        rule = CreateRule(text, action, matchSubdomains ? null : domain, matchSubdomains ? domain : null, pattern: null, requiredLiterals: null, patternSuffix: null, key, modifiers);
        return true;
    }

    private static bool TryBuildRegexRule(
        string text,
        string regexPattern,
        DnsFilterAction action,
        string? modifiersPart,
        string[]? requiredLiterals,
        string? patternSuffix,
        [NotNullWhen(true)] out DnsFilterRule? rule,
        out DnsFilterParseError error,
        out string? detail)
    {
        rule = null;

        if (!TryParseModifiers(modifiersPart, out var modifiers, out error, out detail))
            return false;

        var regex = TryCreateRegex(regexPattern);
        if (regex is null)
        {
            error = DnsFilterParseError.InvalidRegex;
            detail = regexPattern;
            return false;
        }

        var key = (action is DnsFilterAction.Allow ? "@@" : "") + "r:" + regexPattern;
        rule = CreateRule(text, action, exactDomain: null, domainSuffix: null, regex, requiredLiterals, patternSuffix, key, modifiers);
        return true;
    }

    private static DnsFilterRule CreateRule(
        string text,
        DnsFilterAction action,
        string? exactDomain,
        string? domainSuffix,
        Regex? pattern,
        string[]? requiredLiterals,
        string? patternSuffix,
        string key,
        ModifierSet modifiers)
    {
        return new DnsFilterRule
        {
            OriginalText = text,
            Action = action,
            IsImportant = modifiers.IsImportant,
            IsBadFilter = modifiers.IsBadFilter,
            ExactDomain = exactDomain,
            DomainSuffix = domainSuffix,
            Pattern = pattern,
            RequiredLiterals = requiredLiterals,
            PatternSuffix = patternSuffix,
            BadFilterKey = key + modifiers.CanonicalSuffix,
            AllowedDnsTypes = modifiers.AllowedDnsTypes,
            ExcludedDnsTypes = modifiers.ExcludedDnsTypes,
            DenyAllowDomains = modifiers.DenyAllowDomains,
            Rewrite = modifiers.Rewrite,
            ClientSpecs = modifiers.ClientSpecs,
            TagSpec = modifiers.TagSpec,
        };
    }

    /// <summary>
    /// Turns an Adblock wildcard pattern into a regex that encodes its anchors, instead of escaping
    /// them as literal characters.
    /// </summary>
    private static string BuildWildcardRegex(string pattern, bool matchSubdomains)
    {
        var escaped = Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal);

        // '||X' matches X itself and anything under it; every other form is matched against the
        // whole name, since a DNS query carries nothing but a name to anchor against.
        return matchSubdomains
            ? "^(?:.*\\.)?" + escaped + "$"
            : "^" + escaped + "$";
    }

    /// <summary>
    /// Returns every literal run of a wildcard pattern that is long enough to be selective. All of
    /// them must appear in a name for the pattern to have any chance of matching, so checking them
    /// up front skips the regex for almost every query.
    /// </summary>
    private static string[]? GetWildcardLiterals(string pattern)
    {
        List<string>? literals = null;
        foreach (var part in pattern.Split('*', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Length >= 3)
            {
                literals ??= [];
                literals.Add(part.ToLowerInvariant());
            }
        }

        return literals?.ToArray();
    }

    /// <summary>
    /// Extracts the literal prefix a regex requires, so a name that cannot possibly match is
    /// rejected by a substring check instead of by running the expression.
    /// </summary>
    /// <remarks>
    /// Returns <see langword="null"/> unless the prefix is genuinely mandatory: a top-level
    /// alternation means an input could match a different branch, and a quantifier means the last
    /// character is optional, so both end the prefix (or discard it entirely).
    /// </remarks>
    internal static string? GetRegexLiteralPrefix(string source)
    {
        if (HasTopLevelAlternation(source))
            return null;

        var index = source.StartsWith('^', StringComparison.Ordinal) ? 1 : 0;
        var builder = new StringBuilder();

        while (index < source.Length)
        {
            var c = source[index];
            char literal;

            if (c is '\\')
            {
                // Only a punctuation escape is a literal; '\d' and friends are classes.
                if (index + 1 >= source.Length || char.IsAsciiLetterOrDigit(source[index + 1]))
                    break;

                literal = source[index + 1];
                index += 2;
            }
            else if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            {
                literal = c;
                index++;
            }
            else
            {
                break;
            }

            // A quantifier makes the character it follows optional, so it cannot be required.
            if (index < source.Length && source[index] is '*' or '?' or '{')
                break;

            builder.Append(literal);
        }

        return builder.Length >= 3 ? builder.ToString().ToLowerInvariant() : null;
    }

    private static bool HasTopLevelAlternation(string source)
    {
        var depth = 0;
        var inClass = false;

        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            if (c is '\\')
            {
                i++;
                continue;
            }

            if (inClass)
            {
                if (c is ']')
                {
                    inClass = false;
                }

                continue;
            }

            switch (c)
            {
                case '[': inClass = true; break;
                case '(': depth++; break;
                case ')': depth--; break;
                case '|' when depth <= 0: return true;
                default: break;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the concrete domain suffix a wildcard pattern ends with, when the text after the
    /// last <c>*</c> begins at a label boundary and covers at least two labels. That suffix is
    /// specific enough to index on; a single label such as <c>com</c> is not.
    /// </summary>
    private static string? GetPatternSuffix(string pattern)
    {
        var lastStar = pattern.LastIndexOf('*', StringComparison.Ordinal);
        if (lastStar < 0)
            return null;

        var tail = pattern[(lastStar + 1)..];
        if (!tail.StartsWith('.', StringComparison.Ordinal))
            return null;

        tail = tail[1..].TrimEnd('^');
        if (!tail.Contains('.', StringComparison.Ordinal))
            return null;

        return DnsDomainName.TryNormalize(tail, out var normalized) ? normalized : null;
    }

    private static Regex? TryCreateRegex(string pattern)
    {
        // NonBacktracking guarantees linear-time matching, which is what makes a hostile pattern in
        // a third-party list harmless. Not every construct is supported, so fall back to the
        // backtracking engine (still bounded by the match timeout) when it is not.
        try
        {
            return new Regex(pattern, RegexOptions.NonBacktracking | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (NotSupportedException)
        {
        }
        catch (ArgumentException)
        {
            return null;
        }

        try
        {
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool TryParseModifiers(
        string? modifiersPart,
        out ModifierSet modifiers,
        out DnsFilterParseError error,
        out string? detail)
    {
        modifiers = default;
        error = DnsFilterParseError.UnsupportedModifier;
        detail = null;

        if (modifiersPart is null)
        {
            modifiers = new ModifierSet { CanonicalSuffix = "" };
            return true;
        }

        var isImportant = false;
        var isBadFilter = false;
        IReadOnlyCollection<DnsFilterQueryType>? allowedDnsTypes = null;
        IReadOnlyCollection<DnsFilterQueryType>? excludedDnsTypes = null;
        IReadOnlyCollection<string>? denyAllowDomains = null;
        DnsFilterRewriteRule? rewrite = null;
        IReadOnlyList<DnsFilterClientSpec>? clientSpecs = null;
        DnsFilterTagSpec? tagSpec = null;
        var canonical = new List<string>();

        foreach (var mod in SplitRespectingQuotes(modifiersPart, ','))
        {
            if (mod.Length is 0)
                continue;

            if (mod.Equals("important", StringComparison.OrdinalIgnoreCase))
            {
                isImportant = true;
                canonical.Add("important");
            }
            else if (mod.Equals("badfilter", StringComparison.OrdinalIgnoreCase))
            {
                // Deliberately not part of the canonical key: a $badfilter rule must produce the
                // same key as the rule it disables.
                isBadFilter = true;
            }
            else if (mod.StartsWith("dnstype=", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseDnsTypeModifier(mod["dnstype=".Length..], out allowedDnsTypes, out excludedDnsTypes, out var bad))
                {
                    error = DnsFilterParseError.InvalidModifierValue;
                    detail = "dnstype=" + bad;
                    return false;
                }

                canonical.Add("dnstype=" + Canonicalize(allowedDnsTypes, excludedDnsTypes));
            }
            else if (mod.StartsWith("denyallow=", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseDenyAllow(mod["denyallow=".Length..], out denyAllowDomains, out var bad))
                {
                    error = DnsFilterParseError.InvalidModifierValue;
                    detail = "denyallow=" + bad;
                    return false;
                }

                canonical.Add("denyallow=" + string.Join('|', denyAllowDomains.Order(StringComparer.Ordinal)));
            }
            else if (mod.StartsWith("dnsrewrite=", StringComparison.OrdinalIgnoreCase))
            {
                var value = mod["dnsrewrite=".Length..];
                rewrite = ParseDnsRewrite(value);
                if (rewrite is null)
                {
                    error = DnsFilterParseError.InvalidModifierValue;
                    detail = "dnsrewrite=" + value;
                    return false;
                }

                canonical.Add("dnsrewrite=" + value.ToLowerInvariant());
            }
            else if (mod.StartsWith("client=", StringComparison.OrdinalIgnoreCase))
            {
                var value = mod["client=".Length..];
                clientSpecs = ParseClientSpecs(value);
                if (clientSpecs is null)
                {
                    error = DnsFilterParseError.InvalidModifierValue;
                    detail = "client=" + value;
                    return false;
                }

                canonical.Add("client=" + value.ToLowerInvariant());
            }
            else if (mod.StartsWith("ctag=", StringComparison.OrdinalIgnoreCase))
            {
                var value = mod["ctag=".Length..];
                tagSpec = ParseCtagModifier(value);
                if (tagSpec is null)
                {
                    error = DnsFilterParseError.InvalidModifierValue;
                    detail = "ctag=" + value;
                    return false;
                }

                canonical.Add("ctag=" + value.ToLowerInvariant());
            }
            else
            {
                // Unknown modifier: discard the rule rather than silently applying it unscoped.
                error = DnsFilterParseError.UnsupportedModifier;
                detail = mod;
                return false;
            }
        }

        canonical.Sort(StringComparer.Ordinal);

        modifiers = new ModifierSet
        {
            IsImportant = isImportant,
            IsBadFilter = isBadFilter,
            AllowedDnsTypes = allowedDnsTypes,
            ExcludedDnsTypes = excludedDnsTypes,
            DenyAllowDomains = denyAllowDomains,
            Rewrite = rewrite,
            ClientSpecs = clientSpecs,
            TagSpec = tagSpec,
            CanonicalSuffix = canonical.Count is 0 ? "" : "$" + string.Join(',', canonical),
        };

        return true;
    }

    private static string Canonicalize(IReadOnlyCollection<DnsFilterQueryType>? allowed, IReadOnlyCollection<DnsFilterQueryType>? excluded)
    {
        var parts = new List<string>();
        if (allowed is not null)
        {
            parts.AddRange(allowed.Select(t => ((ushort)t).ToString(CultureInfo.InvariantCulture)));
        }

        if (excluded is not null)
        {
            parts.AddRange(excluded.Select(t => "~" + ((ushort)t).ToString(CultureInfo.InvariantCulture)));
        }

        parts.Sort(StringComparer.Ordinal);
        return string.Join('|', parts);
    }

    /// <summary>
    /// Splits on <paramref name="separator"/> while honouring quoted sections and backslash escapes.
    /// An unterminated quote is treated as a literal character rather than swallowing the rest of
    /// the value.
    /// </summary>
    private static List<string> SplitRespectingQuotes(string value, char separator)
    {
        var result = new List<string>();
        if (!TrySplit(value, separator, respectQuotes: true, result))
        {
            result.Clear();
            TrySplit(value, separator, respectQuotes: false, result);
        }

        return result;
    }

    private static bool TrySplit(string value, char separator, bool respectQuotes, List<string> result)
    {
        var start = 0;
        var inQuote = false;
        var quoteChar = '\0';

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (inQuote)
            {
                if (c is '\\' && i + 1 < value.Length)
                {
                    i++;
                }
                else if (c == quoteChar)
                {
                    inQuote = false;
                }
            }
            else if (respectQuotes && c is '\'' or '"')
            {
                inQuote = true;
                quoteChar = c;
            }
            else if (c is '\\' && i + 1 < value.Length)
            {
                i++;
            }
            else if (c == separator)
            {
                AddSegment(result, value[start..i]);
                start = i + 1;
            }
        }

        if (inQuote)
            return false;

        AddSegment(result, value[start..]);
        return true;
    }

    private static void AddSegment(List<string> result, string segment)
    {
        var trimmed = segment.Trim();
        if (trimmed.Length > 0)
        {
            result.Add(trimmed);
        }
    }

    private static bool TryParseDnsTypeModifier(
        string value,
        out IReadOnlyCollection<DnsFilterQueryType>? allowed,
        out IReadOnlyCollection<DnsFilterQueryType>? excluded,
        out string? invalidToken)
    {
        allowed = null;
        excluded = null;
        invalidToken = null;

        var allowedList = new List<DnsFilterQueryType>();
        var excludedList = new List<DnsFilterQueryType>();

        var parts = SplitRespectingQuotes(value, '|');
        if (parts.Count is 0)
        {
            invalidToken = "";
            return false;
        }

        foreach (var part in parts)
        {
            var token = part;
            var isExclusion = token.StartsWith('~', StringComparison.Ordinal);
            if (isExclusion)
            {
                token = token[1..];
            }

            if (!TryParseQueryType(token, out var queryType))
            {
                invalidToken = part;
                return false;
            }

            if (isExclusion)
            {
                excludedList.Add(queryType);
            }
            else
            {
                allowedList.Add(queryType);
            }
        }

        allowed = allowedList.Count > 0 ? allowedList : null;
        excluded = excludedList.Count > 0 ? excludedList : null;
        return allowed is not null || excluded is not null;
    }

    private static bool TryParseQueryType(string token, out DnsFilterQueryType queryType)
    {
        queryType = default;
        if (token.Length is 0)
            return false;

        // Named types. Enum.TryParse also accepts numbers and comma-separated lists, neither of
        // which is valid here, so require the token to be non-numeric before trusting it.
        if (!char.IsAsciiDigit(token[0]) && Enum.TryParse(token, ignoreCase: true, out queryType) && !token.Contains(',', StringComparison.Ordinal))
            return true;

        // Numeric forms: '65' and 'TYPE65'. Unnamed types are legitimate; the enum is an open set.
        var numeric = token.StartsWith("TYPE", StringComparison.OrdinalIgnoreCase) ? token[4..] : token;
        if (ushort.TryParse(numeric, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            queryType = (DnsFilterQueryType)value;
            return true;
        }

        return false;
    }

    private static bool TryParseDenyAllow(string value, [NotNullWhen(true)] out IReadOnlyCollection<string>? domains, out string? invalidToken)
    {
        domains = null;
        invalidToken = null;

        var parts = SplitRespectingQuotes(value, '|');
        if (parts.Count is 0)
        {
            invalidToken = "";
            return false;
        }

        var result = new List<string>(parts.Count);
        foreach (var part in parts)
        {
            if (!DnsDomainName.TryNormalize(part, out var domain))
            {
                invalidToken = part;
                return false;
            }

            result.Add(domain);
        }

        domains = result;
        return true;
    }

    private static DnsFilterRewriteRule? ParseDnsRewrite(string value)
    {
        if (value.Length is 0)
            return null;

        if (TryParseResponseCode(value, out var shorthandRcode))
            return new DnsFilterRewriteRule { ResponseCode = shorthandRcode };

        // Shorthand IP. Require a '.' or ':' first: IPAddress.TryParse otherwise accepts bare
        // integers ("1234" becomes 0.0.4.210), which would silently produce a bogus A record.
        if (LooksLikeIPAddress(value) && IPAddress.TryParse(value, out var address))
            return CreateAddressRewrite(address);

        var parts = value.Split(';', 3);
        if (parts.Length >= 3)
        {
            if (!TryParseResponseCode(parts[0], out var rcode))
                return null;

            DnsFilterQueryType? recordType = null;
            if (parts[1].Length > 0)
            {
                if (!TryParseQueryType(parts[1], out var parsedType))
                    return null;

                recordType = parsedType;
            }

            var rewriteValue = parts[2].Length > 0 ? parts[2] : null;
            if (!IsRewriteValueValid(recordType, rewriteValue))
                return null;

            return new DnsFilterRewriteRule
            {
                ResponseCode = rcode,
                RecordType = recordType,
                Value = rewriteValue,
            };
        }

        // Shorthand domain: a CNAME rewrite.
        if (DnsDomainName.TryNormalize(value, out var target))
        {
            return new DnsFilterRewriteRule
            {
                ResponseCode = DnsFilterRewriteResponseCode.NoError,
                RecordType = DnsFilterQueryType.CNAME,
                Value = target,
            };
        }

        return null;
    }

    private static DnsFilterRewriteRule CreateAddressRewrite(IPAddress address)
    {
        var isV6 = address.AddressFamily is System.Net.Sockets.AddressFamily.InterNetworkV6;
        return new DnsFilterRewriteRule
        {
            ResponseCode = DnsFilterRewriteResponseCode.NoError,
            RecordType = isV6 ? DnsFilterQueryType.AAAA : DnsFilterQueryType.A,
            Value = address.ToString(),
        };
    }

    private static bool LooksLikeIPAddress(string value)
        => value.Contains('.', StringComparison.Ordinal) || value.Contains(':', StringComparison.Ordinal);

    private static bool TryParseResponseCode(string value, out DnsFilterRewriteResponseCode responseCode)
    {
        responseCode = DnsFilterRewriteResponseCode.NoError;
        if (value.Length is 0)
            return true;

        // Only the DNS spellings are accepted. Enum.TryParse would also accept arbitrary integers
        // and hand callers an undefined enum value.
        if (value.Equals("NOERROR", StringComparison.OrdinalIgnoreCase))
            return true;

        if (value.Equals("NXDOMAIN", StringComparison.OrdinalIgnoreCase) || value.Equals(nameof(DnsFilterRewriteResponseCode.NameError), StringComparison.OrdinalIgnoreCase))
        {
            responseCode = DnsFilterRewriteResponseCode.NameError;
            return true;
        }

        if (value.Equals("REFUSED", StringComparison.OrdinalIgnoreCase))
        {
            responseCode = DnsFilterRewriteResponseCode.Refused;
            return true;
        }

        if (value.Equals("SERVFAIL", StringComparison.OrdinalIgnoreCase) || value.Equals(nameof(DnsFilterRewriteResponseCode.ServerFailure), StringComparison.OrdinalIgnoreCase))
        {
            responseCode = DnsFilterRewriteResponseCode.ServerFailure;
            return true;
        }

        return false;
    }

    private static bool IsRewriteValueValid(DnsFilterQueryType? recordType, string? value)
    {
        if (recordType is null || value is null)
            return true;

        return recordType switch
        {
            DnsFilterQueryType.A => IPAddress.TryParse(value, out var v4) && v4.AddressFamily is System.Net.Sockets.AddressFamily.InterNetwork,
            DnsFilterQueryType.AAAA => IPAddress.TryParse(value, out var v6) && v6.AddressFamily is System.Net.Sockets.AddressFamily.InterNetworkV6,
            DnsFilterQueryType.CNAME or DnsFilterQueryType.PTR or DnsFilterQueryType.NS or DnsFilterQueryType.DNAME => DnsDomainName.TryNormalize(value, out _),
            _ => true,
        };
    }

    private static List<DnsFilterClientSpec>? ParseClientSpecs(string value)
    {
        var parts = SplitRespectingQuotes(value, '|');
        if (parts.Count is 0)
            return null;

        var specs = new List<DnsFilterClientSpec>(parts.Count);
        foreach (var part in parts)
        {
            var token = part;
            var isExclusion = token.StartsWith('~', StringComparison.Ordinal);
            if (isExclusion)
            {
                token = token[1..];
            }

            token = UnquoteClientName(token);
            if (token.Length is 0)
                return null;

            if (IPAddress.TryParse(token, out var ip))
            {
                specs.Add(new DnsFilterClientSpec { IsExclusion = isExclusion, Address = ip });
                continue;
            }

            if (token.Contains('/', StringComparison.Ordinal))
            {
                // A value that looks like a CIDR but does not parse is a mistake, not a client name.
                if (!IPNetwork.TryParse(token, out var network))
                    return null;

                specs.Add(new DnsFilterClientSpec { IsExclusion = isExclusion, Network = network });
                continue;
            }

            specs.Add(new DnsFilterClientSpec { IsExclusion = isExclusion, Name = token });
        }

        return specs;
    }

    private static string UnquoteClientName(string name)
    {
        var inner = name;
        if (name.Length >= 2 &&
            ((name.StartsWith('\'', StringComparison.Ordinal) && name.EndsWith('\'', StringComparison.Ordinal)) ||
             (name.StartsWith('"', StringComparison.Ordinal) && name.EndsWith('"', StringComparison.Ordinal))))
        {
            inner = name[1..^1];
        }

        if (!inner.Contains('\\', StringComparison.Ordinal))
            return inner;

        var builder = new StringBuilder(inner.Length);
        for (var i = 0; i < inner.Length; i++)
        {
            if (inner[i] is '\\' && i + 1 < inner.Length && inner[i + 1] is '\'' or '"' or ',' or '|' or '\\')
            {
                i++;
            }

            builder.Append(inner[i]);
        }

        return builder.ToString();
    }

    private static DnsFilterTagSpec? ParseCtagModifier(string value)
    {
        var parts = SplitRespectingQuotes(value, '|');
        if (parts.Count is 0)
            return null;

        var included = new List<string>();
        var excluded = new List<string>();

        foreach (var part in parts)
        {
            if (part.StartsWith('~', StringComparison.Ordinal))
            {
                if (part.Length is 1)
                    return null;

                excluded.Add(part[1..]);
            }
            else
            {
                included.Add(part);
            }
        }

        return new DnsFilterTagSpec
        {
            IncludedTags = included.Count > 0 ? included : null,
            ExcludedTags = excluded.Count > 0 ? excluded : null,
        };
    }

    private enum LineKind
    {
        Empty,
        Comment,
        AdBlockComment,
        AdBlockRule,
        HostsEntry,
        Domain,
    }

    private readonly struct ModifierSet
    {
        public bool IsImportant { get; init; }
        public bool IsBadFilter { get; init; }
        public IReadOnlyCollection<DnsFilterQueryType>? AllowedDnsTypes { get; init; }
        public IReadOnlyCollection<DnsFilterQueryType>? ExcludedDnsTypes { get; init; }
        public IReadOnlyCollection<string>? DenyAllowDomains { get; init; }
        public DnsFilterRewriteRule? Rewrite { get; init; }
        public IReadOnlyList<DnsFilterClientSpec>? ClientSpecs { get; init; }
        public DnsFilterTagSpec? TagSpec { get; init; }
        public string CanonicalSuffix { get; init; }
    }
}
