using System.Text.RegularExpressions;

namespace Meziantou.Framework.SyntaxHighlighting.Engine;

internal static class Compiler
{
    /// <summary>
    /// The default upper bound for a single regex match: none. A finite timeout makes every match about twice as slow,
    /// and a fixed bound would also cut off legitimately long scans of large documents on a busy machine, so it is
    /// opt-in (<see cref="HighlightOptions.MatchTimeout"/>). <see cref="Tokenizer"/> turns a timeout into the plain-text fallback.
    /// </summary>
    internal static readonly TimeSpan DefaultMatchTimeout = Timeout.InfiniteTimeSpan;

    private const string UseCompiledRegexSwitchName = "Meziantou.Framework.SyntaxHighlighting.UseCompiledRegex";

    public static CompiledMode Compile(Mode language) => Compile(language, DefaultMatchTimeout);

    public static CompiledMode Compile(Mode language, TimeSpan matchTimeout)
    {
        var context = new CompilationContext(language.CaseInsensitive, language.ClassNameAliases, GetRegexOptions(language.CaseInsensitive), matchTimeout);
        var root = CompileMode(language, isRoot: true, context);
        root.RegexSlotCount = context.SlotCount;
        root.MatchTimeout = matchTimeout;
        return root;
    }

    private static RegexOptions GetRegexOptions(bool caseInsensitive)
    {
        var options = RegexOptions.Multiline | RegexOptions.CultureInvariant;
        if (caseInsensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }

        // Compiled regexes cost ~100 ms per grammar to build but scan several times faster, which
        // pays off in long-running processes that highlight a lot of code.
        if (AppContext.TryGetSwitch(UseCompiledRegexSwitchName, out var useCompiled) && useCompiled)
        {
            options |= RegexOptions.Compiled;
        }

        return options;
    }

    private static CompiledMode CompileMode(Mode mode, bool isRoot, CompilationContext context)
    {
        if (context.Memo.TryGetValue(mode, out var existing))
            return existing;

        Validate(mode);

        var cmode = new CompiledMode
        {
            Source = mode,
            Scope = mode.Scope,
            ExcludeBegin = mode.ExcludeBegin,
            ExcludeEnd = mode.ExcludeEnd,
            ReturnBegin = mode.ReturnBegin,
            ReturnEnd = mode.ReturnEnd,
            EndsWithParent = mode.EndsWithParent,
            EndsParent = mode.EndsParent,
            ClassNameAliases = context.Aliases,
            SubLanguage = mode.SubLanguage,
            EndSameAsBegin = mode.EndSameAsBegin,
            BeginGuard = mode.BeginGuard,
            EndScope = mode.EndScope,
            KeywordValidator = mode.KeywordValidator,
            Skip = mode.Skip,
        };
        context.Memo[mode] = cmode;

        // match → begin alias; handle multi-part begin arrays
        string? begin;
        if (mode.BeginParts is { Count: > 0 } parts)
        {
            var sb = new StringBuilder();
            var order = new List<int>(parts.Count);
            var groupIndex = 1;
            foreach (var part in parts)
            {
                sb.Append('(').Append(part).Append(')');
                order.Add(groupIndex);
                groupIndex += 1 + CountCapturingGroups(part);
            }

            begin = sb.ToString();
            cmode.BeginGroupOrder = order;

            if (mode.BeginScope is not null)
            {
                var remapped = new Dictionary<int, string>();
                foreach (var (partIndex, scope) in mode.BeginScope)
                {
                    if (partIndex >= 1 && partIndex <= order.Count)
                        remapped[order[partIndex - 1]] = scope;
                }

                cmode.BeginGroupScopes = remapped;
            }
        }
        else
        {
            begin = mode.Begin ?? mode.Match;
        }

        var end = mode.End;
        var keywords = mode.Keywords;

        // beginKeywords sugar — `(?<!\.)` mirrors hljs's skipIfHasPrecedingDot,
        // which prevents matches like `foo.catch(` from being treated as a keyword.
        if (mode.BeginKeywords is { Count: > 0 } beginKeywords)
        {
            begin = @"(?<!\.)\b(" + string.Join('|', beginKeywords.Select(Regex.Escape)) + @")(?!\.)(?=\b|\s)";
            keywords ??= Keywords.FromWords(beginKeywords);
        }

        // Defaults for child modes when neither begin nor end is set
        if (!isRoot)
        {
            if (string.IsNullOrEmpty(begin))
                begin = @"\B|\b";
            if (string.IsNullOrEmpty(end) && !mode.EndsWithParent)
                end = @"\B|\b";
        }

        if (!string.IsNullOrEmpty(begin))
        {
            cmode.BeginRe = CreateRegex(begin, context);
            cmode.BeginSlot = context.SlotCount++;
        }

        if (!string.IsNullOrEmpty(end))
        {
            cmode.EndRe = CreateRegex(end, context);
            cmode.EndSlot = context.SlotCount++;
        }

        if (!string.IsNullOrEmpty(mode.Illegal))
        {
            cmode.IllegalRe = CreateRegex(mode.Illegal, context);
            cmode.IllegalSlot = context.SlotCount++;
        }

        if (keywords is not null)
        {
            cmode.KeywordPatternRe = CreateRegex(mode.KeywordPattern ?? @"\w+", context);
            cmode.KeywordMap = BuildKeywordMap(keywords, context.CaseInsensitive);
        }

        // Compile children, expanding 'self' and 'variants' as we go
        foreach (var raw in mode.Contains)
        {
            foreach (var expanded in Expand(raw, mode, context))
            {
                var child = CompileMode(expanded, isRoot: false, context);
                cmode.Contains.Add(child);
            }
        }

        if (mode.Starts is not null)
            cmode.Starts = CompileMode(mode.Starts, isRoot, context);

        return cmode;
    }

    /// <summary>
    /// Rejects flag combinations that would make the tokenizer emit a lexeme and then scan it again.
    /// highlight.js rejects most of them too; the tokenizer relies on it (its mode buffer is always a
    /// contiguous range of the input).
    /// </summary>
    private static void Validate(Mode mode)
    {
        if (mode.ExcludeBegin && mode.ReturnBegin)
            throw new InvalidOperationException("A mode cannot combine ExcludeBegin and ReturnBegin.");

        if (mode.ExcludeEnd && mode.ReturnEnd)
            throw new InvalidOperationException("A mode cannot combine ExcludeEnd and ReturnEnd.");

        if (mode.BeginScope is not null && (mode.ExcludeBegin || mode.ReturnBegin || mode.Skip))
            throw new InvalidOperationException("A mode with BeginScope cannot use ExcludeBegin, ReturnBegin or Skip.");

        if (mode.EndScope is not null && (mode.ExcludeEnd || mode.ReturnEnd))
            throw new InvalidOperationException("A mode with EndScope cannot use ExcludeEnd or ReturnEnd.");
    }

    private static Regex CreateRegex(string pattern, CompilationContext context) => new(pattern, context.RegexOptions, context.MatchTimeout);

    private static IReadOnlyList<Mode> Expand(Mode mode, Mode enclosing, CompilationContext context)
    {
        if (ReferenceEquals(mode, Mode.Self))
            return [enclosing];

        // Cache the expanded variants per source Mode so that recursive grammars
        // (e.g. Razor, where `m13` and `m11` reference each other) produce the
        // same expanded instances on every visit, allowing the CompileMode memo
        // to short-circuit cycles.
        if (context.ExpandCache.TryGetValue(mode, out var cached))
            return cached;

        IReadOnlyList<Mode> result;
        if (mode.Variants is { Count: > 0 } variants)
        {
            var list = new List<Mode>(variants.Count);
            foreach (var variant in variants)
            {
                list.Add(new Mode(mode)
                {
                    Variants = null,
                    Scope = variant.ClearScope ? null : variant.Scope ?? mode.Scope,
                    Match = variant.Match ?? mode.Match,
                    Begin = variant.Begin ?? mode.Begin,
                    End = variant.End ?? mode.End,
                    EndScope = variant.EndScope ?? mode.EndScope,
                    BeginParts = variant.BeginParts ?? mode.BeginParts,
                    BeginScope = variant.BeginScope ?? mode.BeginScope,
                    BeginGuard = variant.BeginGuard ?? mode.BeginGuard,
                    SubLanguage = variant.SubLanguage ?? mode.SubLanguage,
                    BeginKeywords = variant.BeginKeywords ?? mode.BeginKeywords,
                    Illegal = variant.Illegal ?? mode.Illegal,
                    Keywords = variant.Keywords ?? mode.Keywords,
                    KeywordPattern = variant.KeywordPattern ?? mode.KeywordPattern,
                    KeywordValidator = variant.KeywordValidator ?? mode.KeywordValidator,
                    Contains = variant.Contains.Count > 0 ? variant.Contains : mode.Contains,
                    Starts = variant.Starts ?? mode.Starts,
                    ExcludeBegin = variant.ExcludeBegin || mode.ExcludeBegin,
                    ExcludeEnd = variant.ExcludeEnd || mode.ExcludeEnd,
                    ReturnBegin = variant.ReturnBegin || mode.ReturnBegin,
                    ReturnEnd = variant.ReturnEnd || mode.ReturnEnd,
                    EndsWithParent = variant.EndsWithParent || mode.EndsWithParent,
                    EndsParent = variant.EndsParent || mode.EndsParent,
                    EndSameAsBegin = variant.EndSameAsBegin || mode.EndSameAsBegin,
                    Skip = variant.Skip || mode.Skip,
                });
            }

            result = list;
        }
        else
        {
            result = [mode];
        }

        context.ExpandCache[mode] = result;
        return result;
    }

    /// <summary>
    /// Counts the numbered capturing groups of one <see cref="Mode.BeginParts"/> entry, so the
    /// group numbers of the concatenated pattern can be mapped back to parts.
    /// </summary>
    /// <remarks>
    /// .NET numbers named groups after every unnamed group of the whole pattern, which would break
    /// that mapping, so named groups are rejected rather than silently mis-assigned.
    /// </remarks>
    private static int CountCapturingGroups(string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
        var names = regex.GetGroupNames();
        if (names.Any(name => !int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            throw new InvalidOperationException($"BeginParts entries cannot contain named groups: '{pattern}'");

        return regex.GetGroupNumbers().Length - 1;
    }

    private static Dictionary<string, string?> BuildKeywordMap(Keywords keywords, bool caseInsensitive)
    {
        var map = new Dictionary<string, string?>(caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        // Groups are processed in declaration order and a later group intentionally overrides an
        // earlier one for the same word, so a specific scope (type/literal/built_in) can take
        // precedence over the generic `keyword` group. See Keywords for the full contract.
        foreach (var (scope, words) in keywords.Groups)
        {
            // `_` is highlight.js's sentinel for "a word of the language, but not highlighted".
            var keywordScope = scope is "_" ? null : scope;

            foreach (var raw in words)
            {
                // highlight.js keyword entries may carry a relevance suffix (`const_cast|10`). The
                // relevance only matters for language auto-detection, which is not supported.
                var separator = raw.IndexOf('|', StringComparison.Ordinal);
                var word = separator < 0 ? raw : raw[..separator];
                map[word] = keywordScope;
            }
        }

        return map;
    }

    private sealed class CompilationContext(bool caseInsensitive, IReadOnlyDictionary<string, string>? aliases, RegexOptions regexOptions, TimeSpan matchTimeout)
    {
        public TimeSpan MatchTimeout { get; } = matchTimeout;

        public bool CaseInsensitive { get; } = caseInsensitive;

        public IReadOnlyDictionary<string, string>? Aliases { get; } = aliases;

        public RegexOptions RegexOptions { get; } = regexOptions;

        public Dictionary<Mode, CompiledMode> Memo { get; } = new(ReferenceEqualityComparer.Instance);

        public Dictionary<Mode, IReadOnlyList<Mode>> ExpandCache { get; } = new(ReferenceEqualityComparer.Instance);

        public int SlotCount { get; set; }
    }
}
