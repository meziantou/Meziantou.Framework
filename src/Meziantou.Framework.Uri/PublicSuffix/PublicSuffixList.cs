using System.Collections.Frozen;

namespace Meziantou.Framework;

/// <summary>
/// Provides access to an embedded snapshot of the <see href="https://publicsuffix.org/list/">Public Suffix List</see>,
/// which describes the part of a domain name that is not under the control of an individual registrant.
/// </summary>
/// <remarks>
/// The list is compiled into the assembly, so no network access is performed. It is refreshed when a new version of the package is released.
/// </remarks>
public static partial class PublicSuffixList
{
    // A domain name can contain at most 127 labels
    private const int MaxLabelCount = 128;

    // A fully-qualified domain name can contain at most 253 characters
    private const int MaxDomainLength = 253;

    private static readonly IdnMapping IdnMapping = new();
    private static readonly FrozenDictionary<string, PublicSuffixRuleFlags> Rules = LoadRules();
    private static readonly FrozenDictionary<string, PublicSuffixRuleFlags>.AlternateLookup<ReadOnlySpan<char>> RulesLookup = Rules.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>The number of rules of the embedded list.</summary>
    public static int RuleCount => RuleCountValue;

    /// <summary>The date of the commit the embedded list was generated from.</summary>
    public static DateTimeOffset LastUpdated => new(LastUpdatedTicks, TimeSpan.Zero);

    /// <summary>
    /// Determines whether the domain is a public suffix, that is whether it matches a rule of the list.
    /// </summary>
    /// <remarks>Returns <see langword="false"/> for an unlisted top-level domain, even though the implicit <c>*</c> rule makes it a public suffix.</remarks>
    public static bool IsPublicSuffix(string? domain, PublicSuffixRuleSources sources = PublicSuffixRuleSources.All) => IsPublicSuffix(domain.AsSpan(), sources);

    /// <inheritdoc cref="IsPublicSuffix(string?, PublicSuffixRuleSources)"/>
    public static bool IsPublicSuffix(ReadOnlySpan<char> domain, PublicSuffixRuleSources sources = PublicSuffixRuleSources.All)
        => TryGetDomainInfo(domain, out var domainInfo, sources) && domainInfo.IsKnownPublicSuffix && domainInfo.RegistrableDomain is null;

    /// <summary>
    /// Gets the public suffix (eTLD) of the domain, or <see langword="null"/> when the domain is not a valid domain name.
    /// </summary>
    /// <remarks>When the domain matches no rule, the implicit <c>*</c> rule applies and the top-level domain is returned.</remarks>
    public static string? GetPublicSuffix(string? domain, PublicSuffixRuleSources sources = PublicSuffixRuleSources.All) => GetPublicSuffix(domain.AsSpan(), sources);

    /// <inheritdoc cref="GetPublicSuffix(string?, PublicSuffixRuleSources)"/>
    public static string? GetPublicSuffix(ReadOnlySpan<char> domain, PublicSuffixRuleSources sources = PublicSuffixRuleSources.All)
        => TryGetDomainInfo(domain, out var domainInfo, sources) ? domainInfo.PublicSuffix : null;

    /// <summary>
    /// Gets the registrable domain (eTLD+1) of the domain, or <see langword="null"/> when the domain is not a valid domain name or is itself a public suffix.
    /// </summary>
    public static string? GetRegistrableDomain(string? domain, PublicSuffixRuleSources sources = PublicSuffixRuleSources.All) => GetRegistrableDomain(domain.AsSpan(), sources);

    /// <inheritdoc cref="GetRegistrableDomain(string?, PublicSuffixRuleSources)"/>
    public static string? GetRegistrableDomain(ReadOnlySpan<char> domain, PublicSuffixRuleSources sources = PublicSuffixRuleSources.All)
        => TryGetDomainInfo(domain, out var domainInfo, sources) ? domainInfo.RegistrableDomain : null;

    /// <summary>
    /// Decomposes the domain into its public suffix, registrable domain, and subdomain.
    /// </summary>
    /// <returns><see langword="false"/> when the domain is not a valid domain name.</returns>
    public static bool TryGetDomainInfo(string? domain, out DomainInfo domainInfo, PublicSuffixRuleSources sources = PublicSuffixRuleSources.All)
        => TryGetDomainInfo(domain.AsSpan(), out domainInfo, sources);

    /// <summary>
    /// Decomposes the host of the URI into its public suffix, registrable domain, and subdomain.
    /// </summary>
    /// <returns><see langword="false"/> when the host is not a domain name, such as an IP address.</returns>
    public static bool TryGetDomainInfo(Uri uri, out DomainInfo domainInfo, PublicSuffixRuleSources sources = PublicSuffixRuleSources.All)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (uri.HostNameType is not UriHostNameType.Dns)
        {
            domainInfo = default;
            return false;
        }

        return TryGetDomainInfo(uri.Host.AsSpan(), out domainInfo, sources);
    }

    /// <inheritdoc cref="TryGetDomainInfo(string?, out DomainInfo, PublicSuffixRuleSources)"/>
    public static bool TryGetDomainInfo(ReadOnlySpan<char> domain, out DomainInfo domainInfo, PublicSuffixRuleSources sources = PublicSuffixRuleSources.All)
    {
        domainInfo = default;
        if (sources is PublicSuffixRuleSources.None)
            return false;

        // Accept fully-qualified domain names such as "www.example.com."
        if (domain.Length > 0 && domain[^1] is '.')
            domain = domain[..^1];

        if (domain.IsEmpty || domain.Length > MaxDomainLength)
            return false;

        var normalized = domain.ToString().ToLowerInvariant();

        Span<int> labelStarts = stackalloc int[MaxLabelCount];
        var labelCount = GetLabelStarts(normalized, labelStarts);
        if (labelCount < 0)
            return false;

        Span<int> asciiLabelStarts = stackalloc int[MaxLabelCount];
        if (!TryGetAsciiForm(normalized, labelStarts[..labelCount], asciiLabelStarts, out var ascii))
            return false;

        var suffixIndex = FindPublicSuffixIndex(ascii, asciiLabelStarts[..labelCount], sources, out var source);
        var isKnownPublicSuffix = suffixIndex >= 0;
        if (!isKnownPublicSuffix)
        {
            // No rule matched, so the prevailing rule is "*"
            suffixIndex = labelCount - 1;
        }

        var publicSuffix = normalized[labelStarts[suffixIndex]..];
        string? registrableDomain = null;
        string? subdomain = null;
        if (suffixIndex > 0)
        {
            registrableDomain = normalized[labelStarts[suffixIndex - 1]..];
            if (suffixIndex > 1)
            {
                // labelStarts[i] is the index right after the dot separating it from the previous label
                subdomain = normalized[..(labelStarts[suffixIndex - 1] - 1)];
            }
        }

        domainInfo = new DomainInfo(normalized, publicSuffix, registrableDomain, subdomain, source, isKnownPublicSuffix);
        return true;
    }

    /// <summary>Returns the index of the first label of the public suffix, or -1 when no rule matches.</summary>
    private static int FindPublicSuffixIndex(string ascii, ReadOnlySpan<int> asciiLabelStarts, PublicSuffixRuleSources sources, out PublicSuffixRuleSources source)
    {
        var allowIcann = (sources & PublicSuffixRuleSources.Icann) is not PublicSuffixRuleSources.None;
        var allowPrivate = (sources & PublicSuffixRuleSources.Private) is not PublicSuffixRuleSources.None;
        var labelCount = asciiLabelStarts.Length;

        var bestIndex = -1;
        var bestLabelCount = -1;
        var bestIsException = false;
        var bestSource = PublicSuffixRuleSources.None;

        void Consider(int suffixIndex, int ruleLabelCount, bool isException, PublicSuffixRuleSources ruleSource)
        {
            if (suffixIndex >= labelCount)
                return;

            if (bestIndex >= 0)
            {
                if (bestIsException != isException)
                {
                    // An exception rule takes priority over any other matching rule
                    if (bestIsException)
                        return;
                }
                else if (ruleLabelCount <= bestLabelCount)
                {
                    return;
                }
            }

            bestIndex = suffixIndex;
            bestLabelCount = ruleLabelCount;
            bestIsException = isException;
            bestSource = ruleSource;
        }

        for (var i = 0; i < labelCount; i++)
        {
            if (!RulesLookup.TryGetValue(ascii.AsSpan(asciiLabelStarts[i]), out var flags))
                continue;

            var exceptionSource = GetSource(flags, PublicSuffixRuleFlags.IcannException, PublicSuffixRuleFlags.PrivateException, allowIcann, allowPrivate);
            if (exceptionSource is not PublicSuffixRuleSources.None)
            {
                // The public suffix of an exception rule is the rule minus its leftmost label
                Consider(i + 1, labelCount - i, isException: true, exceptionSource);
            }

            var ruleSource = GetSource(flags, PublicSuffixRuleFlags.IcannRule, PublicSuffixRuleFlags.PrivateRule, allowIcann, allowPrivate);
            if (ruleSource is not PublicSuffixRuleSources.None)
            {
                Consider(i, labelCount - i, isException: false, ruleSource);
            }

            if (i > 0)
            {
                // A "*.suffix" rule makes the label before the suffix part of the public suffix
                var wildcardSource = GetSource(flags, PublicSuffixRuleFlags.IcannWildcard, PublicSuffixRuleFlags.PrivateWildcard, allowIcann, allowPrivate);
                if (wildcardSource is not PublicSuffixRuleSources.None)
                {
                    Consider(i - 1, labelCount - i + 1, isException: false, wildcardSource);
                }
            }
        }

        source = bestSource;
        return bestIndex;
    }

    private static PublicSuffixRuleSources GetSource(PublicSuffixRuleFlags flags, PublicSuffixRuleFlags icannFlag, PublicSuffixRuleFlags privateFlag, bool allowIcann, bool allowPrivate)
    {
        if (allowIcann && (flags & icannFlag) is not PublicSuffixRuleFlags.None)
            return PublicSuffixRuleSources.Icann;

        if (allowPrivate && (flags & privateFlag) is not PublicSuffixRuleFlags.None)
            return PublicSuffixRuleSources.Private;

        return PublicSuffixRuleSources.None;
    }

    private static bool TryGetAsciiForm(string normalized, ReadOnlySpan<int> labelStarts, Span<int> asciiLabelStarts, out string ascii)
    {
        if (Ascii.IsValid(normalized))
        {
            ascii = normalized;
            labelStarts.CopyTo(asciiLabelStarts);
            return true;
        }

        try
        {
            ascii = IdnMapping.GetAscii(normalized);
        }
        catch (ArgumentException)
        {
            ascii = "";
            return false;
        }

        // The conversion must not change the number of labels
        return GetLabelStarts(ascii, asciiLabelStarts) == labelStarts.Length;
    }

    /// <summary>Returns the number of labels and their start index, or -1 when the value is not a valid domain name.</summary>
    private static int GetLabelStarts(ReadOnlySpan<char> value, Span<int> labelStarts)
    {
        var count = 0;
        var start = 0;
        for (var i = 0; i <= value.Length; i++)
        {
            if (i != value.Length && value[i] is not '.')
            {
                if (!IsValidLabelChar(value[i]))
                    return -1;

                continue;
            }

            if (i == start || count == labelStarts.Length)
                return -1;

            labelStarts[count] = start;
            count++;
            start = i + 1;
        }

        return count;
    }

    // Non-ASCII characters are validated by the punycode conversion
    private static bool IsValidLabelChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' || !Ascii.IsValid(c);

    private static FrozenDictionary<string, PublicSuffixRuleFlags> LoadRules()
    {
        var entries = new Dictionary<string, PublicSuffixRuleFlags>(EntryCount, StringComparer.Ordinal);
        AddRules(entries, IcannRules, PublicSuffixRuleFlags.IcannRule);
        AddRules(entries, PrivateRules, PublicSuffixRuleFlags.PrivateRule);
        AddRules(entries, IcannWildcards, PublicSuffixRuleFlags.IcannWildcard);
        AddRules(entries, PrivateWildcards, PublicSuffixRuleFlags.PrivateWildcard);
        AddRules(entries, IcannExceptions, PublicSuffixRuleFlags.IcannException);
        AddRules(entries, PrivateExceptions, PublicSuffixRuleFlags.PrivateException);
        return entries.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static void AddRules(Dictionary<string, PublicSuffixRuleFlags> entries, ReadOnlySpan<byte> data, PublicSuffixRuleFlags flag)
    {
        foreach (var range in data.Split((byte)'\n'))
        {
            var suffix = data[range];
            if (suffix.IsEmpty)
                continue;

            var key = Encoding.UTF8.GetString(suffix);
            entries.TryGetValue(key, out var flags);
            entries[key] = flags | flag;
        }
    }
}
