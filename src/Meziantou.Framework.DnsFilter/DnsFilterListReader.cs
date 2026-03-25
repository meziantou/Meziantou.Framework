using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.DnsFilter;

/// <summary>
/// Reads DNS filter lists in various formats and produces <see cref="DnsFilterRule"/> instances.
/// Supports hosts files, domains-only lists, and AdGuard/Adblock DNS filtering syntax.
/// </summary>
public static class DnsFilterListReader
{
    /// <summary>
    /// Parses all rules from the specified <see cref="TextReader"/>.
    /// </summary>
    /// <param name="reader">The text reader containing the filter list.</param>
    /// <param name="format">The format of the filter list. Defaults to <see cref="DnsFilterListFormat.AutoDetect"/>.</param>
    /// <returns>A list of parsed filter rules.</returns>
    public static IReadOnlyList<DnsFilterRule> Parse(TextReader reader, DnsFilterListFormat format = DnsFilterListFormat.AutoDetect)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var lines = ReadAllLines(reader);
        if (lines.Count == 0)
        {
            return [];
        }

        if (format == DnsFilterListFormat.AutoDetect)
        {
            format = DetectFormat(lines);
        }

        return format switch
        {
            DnsFilterListFormat.Hosts => ParseHostsFormat(lines),
            DnsFilterListFormat.DomainsOnly => ParseDomainsOnlyFormat(lines),
            DnsFilterListFormat.AdBlock => ParseAdBlockFormat(lines),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown filter list format."),
        };
    }

    /// <summary>
    /// Parses all rules from the specified string.
    /// </summary>
    /// <param name="text">The filter list text.</param>
    /// <param name="format">The format of the filter list. Defaults to <see cref="DnsFilterListFormat.AutoDetect"/>.</param>
    /// <returns>A list of parsed filter rules.</returns>
    public static IReadOnlyList<DnsFilterRule> Parse(string text, DnsFilterListFormat format = DnsFilterListFormat.AutoDetect)
    {
        ArgumentNullException.ThrowIfNull(text);
        using var reader = new StringReader(text);
        return Parse(reader, format);
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

    internal static DnsFilterListFormat DetectFormat(IReadOnlyList<string> lines)
    {
        var hasAdBlockMarkers = false;
        var hasHostsEntries = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            // AdBlock-style comment header
            if (trimmed.StartsWith('[') || trimmed.StartsWith("! ", StringComparison.Ordinal) || trimmed.StartsWith("!--", StringComparison.Ordinal))
            {
                hasAdBlockMarkers = true;
                continue;
            }

            // Standard comment
            if (trimmed.StartsWith('#'))
                continue;

            // AdBlock-style rule patterns
            if (trimmed.StartsWith("||", StringComparison.Ordinal) ||
                trimmed.StartsWith("@@", StringComparison.Ordinal) ||
                (trimmed.StartsWith('/') && trimmed.EndsWith('/') && trimmed.Length > 2) ||
                trimmed.Contains('$', StringComparison.Ordinal))
            {
                hasAdBlockMarkers = true;
                continue;
            }

            // Hosts-file entry: starts with an IP address
            if (IsHostsEntry(trimmed))
            {
                hasHostsEntries = true;
                continue;
            }
        }

        if (hasAdBlockMarkers)
        {
            return DnsFilterListFormat.AdBlock;
        }

        if (hasHostsEntries)
        {
            return DnsFilterListFormat.Hosts;
        }

        return DnsFilterListFormat.DomainsOnly;
    }

    private static bool IsHostsEntry(string line)
    {
        var spaceIndex = line.IndexOfAny([' ', '\t']);
        if (spaceIndex <= 0)
            return false;

        var ipPart = line.AsSpan(0, spaceIndex);
        return IPAddress.TryParse(ipPart, out _);
    }

    private static List<DnsFilterRule> ParseHostsFormat(IReadOnlyList<string> lines)
    {
        var rules = new List<DnsFilterRule>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            // Remove inline comments
            var commentIndex = trimmed.IndexOf('#', StringComparison.Ordinal);
            if (commentIndex >= 0)
            {
                trimmed = trimmed[..commentIndex].TrimEnd();
            }

            // Split by whitespace: IP domain [aliases...]
            var parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;

            // Skip the IP part, add each domain
            for (var i = 1; i < parts.Length; i++)
            {
                var domain = NormalizeDomain(parts[i]);
                if (domain.Length == 0 || domain is "localhost" or "localhost.localdomain" or "local" or "broadcasthost" or "ip6-localhost" or "ip6-loopback" or "ip6-localnet" or "ip6-mcastprefix" or "ip6-allnodes" or "ip6-allrouters" or "ip6-allhosts")
                    continue;

                rules.Add(new DnsFilterRule
                {
                    OriginalText = line,
                    Action = DnsFilterAction.Block,
                    ExactDomain = domain,
                });
            }
        }

        return rules;
    }

    private static List<DnsFilterRule> ParseDomainsOnlyFormat(IReadOnlyList<string> lines)
    {
        var rules = new List<DnsFilterRule>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            // Remove inline comments
            var commentIndex = trimmed.IndexOf('#', StringComparison.Ordinal);
            if (commentIndex >= 0)
            {
                trimmed = trimmed[..commentIndex].TrimEnd();
            }

            if (trimmed.Length == 0)
                continue;

            var domain = NormalizeDomain(trimmed);
            if (domain.Length == 0)
                continue;

            rules.Add(new DnsFilterRule
            {
                OriginalText = line,
                Action = DnsFilterAction.Block,
                ExactDomain = domain,
            });
        }

        return rules;
    }

    private static List<DnsFilterRule> ParseAdBlockFormat(IReadOnlyList<string> lines)
    {
        var rules = new List<DnsFilterRule>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('!') || trimmed.StartsWith('#') || trimmed.StartsWith('['))
                continue;

            if (TryParseAdBlockRule(trimmed, out var rule))
            {
                rules.Add(rule);
            }
        }

        return rules;
    }

    private static bool TryParseAdBlockRule(string text, [NotNullWhen(true)] out DnsFilterRule? rule)
    {
        rule = null;

        var action = DnsFilterAction.Block;
        var remaining = text.AsSpan();

        // Check for exception rule (@@)
        if (remaining.StartsWith("@@", StringComparison.Ordinal))
        {
            action = DnsFilterAction.Allow;
            remaining = remaining[2..];
        }

        // Split off modifiers at the $ sign (but not inside regex)
        string? modifiersPart = null;
        var isRegex = remaining.StartsWith("/", StringComparison.Ordinal) && remaining.Length > 2;

        if (isRegex)
        {
            // Find the closing / for regex, then check for $
            var closingSlash = remaining[1..].IndexOf('/');
            if (closingSlash < 0)
                return false;

            var afterRegex = remaining[(closingSlash + 2)..];
            if (afterRegex.StartsWith("$", StringComparison.Ordinal))
            {
                modifiersPart = afterRegex[1..].ToString();
            }

            remaining = remaining[1..(closingSlash + 1)];
        }
        else
        {
            var dollarIndex = remaining.IndexOf('$');
            if (dollarIndex >= 0)
            {
                modifiersPart = remaining[(dollarIndex + 1)..].ToString();
                remaining = remaining[..dollarIndex];
            }
        }

        // Parse modifiers
        var isImportant = false;
        var isBadFilter = false;
        IReadOnlyCollection<DnsFilterQueryType>? allowedDnsTypes = null;
        IReadOnlyCollection<DnsFilterQueryType>? excludedDnsTypes = null;
        IReadOnlyCollection<string>? denyAllowDomains = null;
        DnsFilterRewriteRule? rewrite = null;
        IReadOnlyList<DnsFilterClientSpec>? clientSpecs = null;
        DnsFilterTagSpec? tagSpec = null;

        if (modifiersPart is not null)
        {
            var modifiers = SplitModifiers(modifiersPart);
            foreach (var mod in modifiers)
            {
                if (mod.Equals("important", StringComparison.OrdinalIgnoreCase))
                {
                    isImportant = true;
                }
                else if (mod.Equals("badfilter", StringComparison.OrdinalIgnoreCase))
                {
                    isBadFilter = true;
                }
                else if (mod.StartsWith("dnstype=", StringComparison.OrdinalIgnoreCase))
                {
                    var typeValue = mod["dnstype=".Length..];
                    ParseDnsTypeModifier(typeValue, out allowedDnsTypes, out excludedDnsTypes);
                }
                else if (mod.StartsWith("denyallow=", StringComparison.OrdinalIgnoreCase))
                {
                    var domainsPart = mod["denyallow=".Length..];
                    denyAllowDomains = domainsPart.Split('|', StringSplitOptions.RemoveEmptyEntries)
                        .Select(d => NormalizeDomain(d))
                        .Where(d => d.Length > 0)
                        .ToArray();
                }
                else if (mod.StartsWith("dnsrewrite=", StringComparison.OrdinalIgnoreCase))
                {
                    var rewriteValue = mod["dnsrewrite=".Length..];
                    rewrite = ParseDnsRewrite(rewriteValue);
                }
                else if (mod.StartsWith("client=", StringComparison.OrdinalIgnoreCase))
                {
                    var clientValue = mod["client=".Length..];
                    clientSpecs = ParseClientSpecs(clientValue);
                }
                else if (mod.StartsWith("ctag=", StringComparison.OrdinalIgnoreCase))
                {
                    var ctagValue = mod["ctag=".Length..];
                    tagSpec = ParseCtagModifier(ctagValue);
                }
            }
        }

        // Parse the pattern
        string? exactDomain = null;
        string? domainSuffix = null;
        Regex? pattern = null;

        if (isRegex)
        {
            try
            {
                pattern = new Regex(remaining.ToString(), RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
        else
        {
            var patternStr = remaining.ToString();

            // ||domain^ — matches domain and all subdomains
            if (patternStr.StartsWith("||", StringComparison.Ordinal))
            {
                var domain = patternStr[2..];
                if (domain.EndsWith('^'))
                {
                    domain = domain[..^1];
                }

                domain = NormalizeDomain(domain);
                if (domain.Length == 0)
                    return false;

                domainSuffix = domain;
            }
            // |domain| — exact match with anchors
            else if (patternStr.StartsWith('|') && patternStr.EndsWith('|'))
            {
                var domain = NormalizeDomain(patternStr[1..^1]);
                if (domain.Length == 0)
                    return false;

                exactDomain = domain;
            }
            // Wildcard patterns (contain *)
            else if (patternStr.Contains('*', StringComparison.Ordinal))
            {
                var regexPattern = "^" + Regex.Escape(patternStr.TrimEnd('^')).Replace("\\*", ".*", StringComparison.Ordinal) + "$";
                try
                {
                    pattern = new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }
            else
            {
                // Plain domain rule
                var domain = patternStr.TrimEnd('^');
                domain = NormalizeDomain(domain);
                if (domain.Length == 0)
                    return false;

                exactDomain = domain;
            }
        }

        rule = new DnsFilterRule
        {
            OriginalText = text,
            Action = action,
            IsImportant = isImportant,
            IsBadFilter = isBadFilter,
            ExactDomain = exactDomain,
            DomainSuffix = domainSuffix,
            Pattern = pattern,
            AllowedDnsTypes = allowedDnsTypes,
            ExcludedDnsTypes = excludedDnsTypes,
            DenyAllowDomains = denyAllowDomains,
            Rewrite = rewrite,
            ClientSpecs = clientSpecs,
            TagSpec = tagSpec,
        };

        return true;
    }

    private static List<string> SplitModifiers(string modifiers)
    {
        // Split by comma, but respect quoted strings for client names
        var result = new List<string>();
        var current = 0;
        var inQuote = false;
        var quoteChar = '\0';

        for (var i = 0; i < modifiers.Length; i++)
        {
            var c = modifiers[i];
            if (inQuote)
            {
                if (c == '\\' && i + 1 < modifiers.Length)
                {
                    i++; // skip escaped char
                }
                else if (c == quoteChar)
                {
                    inQuote = false;
                }
            }
            else if (c is '\'' or '"')
            {
                inQuote = true;
                quoteChar = c;
            }
            else if (c == ',')
            {
                var segment = modifiers[current..i].Trim();
                if (segment.Length > 0)
                {
                    result.Add(segment);
                }

                current = i + 1;
            }
        }

        var last = modifiers[current..].Trim();
        if (last.Length > 0)
        {
            result.Add(last);
        }

        return result;
    }

    private static void ParseDnsTypeModifier(
        string value,
        out IReadOnlyCollection<DnsFilterQueryType>? allowed,
        out IReadOnlyCollection<DnsFilterQueryType>? excluded)
    {
        var allowedList = new List<DnsFilterQueryType>();
        var excludedList = new List<DnsFilterQueryType>();

        var parts = value.Split('|', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            var isExclusion = trimmed.StartsWith('~');
            if (isExclusion)
            {
                trimmed = trimmed[1..];
            }

            if (Enum.TryParse<DnsFilterQueryType>(trimmed, ignoreCase: true, out var queryType))
            {
                if (isExclusion)
                {
                    excludedList.Add(queryType);
                }
                else
                {
                    allowedList.Add(queryType);
                }
            }
        }

        allowed = allowedList.Count > 0 ? allowedList : null;
        excluded = excludedList.Count > 0 ? excludedList : null;
    }

    private static DnsFilterRewriteRule ParseDnsRewrite(string value)
    {
        // Shorthand: just an RCODE keyword
        if (value.Equals("REFUSED", StringComparison.OrdinalIgnoreCase))
        {
            return new DnsFilterRewriteRule { ResponseCode = DnsFilterRewriteResponseCode.Refused };
        }

        if (value.Equals("NXDOMAIN", StringComparison.OrdinalIgnoreCase))
        {
            return new DnsFilterRewriteRule { ResponseCode = DnsFilterRewriteResponseCode.NameError };
        }

        if (value.Equals("NOERROR", StringComparison.OrdinalIgnoreCase))
        {
            return new DnsFilterRewriteRule { ResponseCode = DnsFilterRewriteResponseCode.NoError };
        }

        // Shorthand: IP address or domain name
        if (IPAddress.TryParse(value, out _))
        {
            var isV6 = value.Contains(':', StringComparison.Ordinal);
            return new DnsFilterRewriteRule
            {
                ResponseCode = DnsFilterRewriteResponseCode.NoError,
                RecordType = isV6 ? DnsFilterQueryType.AAAA : DnsFilterQueryType.A,
                Value = value,
            };
        }

        // Full syntax: RCODE;RRTYPE;VALUE
        var parts = value.Split(';');
        if (parts.Length >= 3)
        {
            var rcode = DnsFilterRewriteResponseCode.NoError;
            if (parts[0].Length > 0 && Enum.TryParse<DnsFilterRewriteResponseCode>(parts[0], ignoreCase: true, out var parsedRcode))
            {
                rcode = parsedRcode;
            }
            else if (parts[0].Equals("NXDOMAIN", StringComparison.OrdinalIgnoreCase))
            {
                rcode = DnsFilterRewriteResponseCode.NameError;
            }
            else if (parts[0].Equals("REFUSED", StringComparison.OrdinalIgnoreCase))
            {
                rcode = DnsFilterRewriteResponseCode.Refused;
            }

            DnsFilterQueryType? rrType = null;
            if (parts[1].Length > 0 && Enum.TryParse<DnsFilterQueryType>(parts[1], ignoreCase: true, out var parsedType))
            {
                rrType = parsedType;
            }

            var rewriteValue = parts[2].Length > 0 ? parts[2] : null;

            return new DnsFilterRewriteRule
            {
                ResponseCode = rcode,
                RecordType = rrType,
                Value = rewriteValue,
            };
        }

        // Fallback: treat as domain/CNAME rewrite
        return new DnsFilterRewriteRule
        {
            ResponseCode = DnsFilterRewriteResponseCode.NoError,
            RecordType = DnsFilterQueryType.CNAME,
            Value = value,
        };
    }

    private static List<DnsFilterClientSpec> ParseClientSpecs(string value)
    {
        var specs = new List<DnsFilterClientSpec>();
        var parts = value.Split('|', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            var isExclusion = false;

            if (trimmed.StartsWith('~'))
            {
                isExclusion = true;
                trimmed = trimmed[1..];
            }

            // Remove surrounding quotes
            trimmed = UnquoteClientName(trimmed);

            if (trimmed.Length == 0)
                continue;

            // Try IP address
            if (IPAddress.TryParse(trimmed, out var ip))
            {
                specs.Add(new DnsFilterClientSpec { IsExclusion = isExclusion, Address = ip });
                continue;
            }

            // Try CIDR notation
            var slashIndex = trimmed.IndexOf('/', StringComparison.Ordinal);
            if (slashIndex > 0 && IPNetwork.TryParse(trimmed, out var network))
            {
                specs.Add(new DnsFilterClientSpec { IsExclusion = isExclusion, Network = network });
                continue;
            }

            // Client name
            specs.Add(new DnsFilterClientSpec { IsExclusion = isExclusion, Name = trimmed });
        }

        return specs;
    }

    private static string UnquoteClientName(string name)
    {
        if (name.Length >= 2 && ((name.StartsWith('\'') && name.EndsWith('\'')) || (name.StartsWith('"') && name.EndsWith('"'))))
        {
            var inner = name[1..^1];
            // Unescape
            return inner.Replace("\\'", "'", StringComparison.Ordinal).Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\,", ",", StringComparison.Ordinal).Replace("\\|", "|", StringComparison.Ordinal);
        }

        return name;
    }

    private static DnsFilterTagSpec ParseCtagModifier(string value)
    {
        var included = new List<string>();
        var excluded = new List<string>();

        var parts = value.Split('|', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith('~'))
            {
                excluded.Add(trimmed[1..]);
            }
            else
            {
                included.Add(trimmed);
            }
        }

        return new DnsFilterTagSpec
        {
            IncludedTags = included.Count > 0 ? included : null,
            ExcludedTags = excluded.Count > 0 ? excluded : null,
        };
    }

    private static string NormalizeDomain(string domain)
    {
        // Remove trailing dot, lowercase
        domain = domain.Trim().TrimEnd('.').ToLowerInvariant();
        return domain;
    }
}
