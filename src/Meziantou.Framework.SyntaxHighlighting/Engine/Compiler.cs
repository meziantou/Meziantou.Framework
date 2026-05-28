using System.Text.RegularExpressions;

namespace Meziantou.Framework.SyntaxHighlighting.Engine;

internal static class Compiler
{
    private static readonly TimeSpan RegexTimeout = Timeout.InfiniteTimeSpan;

    private static readonly HashSet<string> CommonKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "of", "and", "for", "in", "not", "or", "if", "then",
        "parent", "list", "value",
    };

    public static CompiledMode Compile(Mode language)
    {
        var caseInsensitive = language.CaseInsensitive;
        var memo = new Dictionary<Mode, CompiledMode>(ReferenceEqualityComparer.Instance);
        var expandCache = new Dictionary<Mode, IReadOnlyList<Mode>>(ReferenceEqualityComparer.Instance);
        return CompileMode(language, parent: null, caseInsensitive, language.ClassNameAliases, memo, expandCache);
    }

    private static CompiledMode CompileMode(Mode mode, CompiledMode? parent, bool caseInsensitive, IReadOnlyDictionary<string, string>? aliases, Dictionary<Mode, CompiledMode> memo, Dictionary<Mode, IReadOnlyList<Mode>> expandCache)
    {
        if (memo.TryGetValue(mode, out var existing))
            return existing;

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
            Parent = parent,
            ClassNameAliases = aliases,
            SubLanguage = mode.SubLanguage,
            EndSameAsBegin = mode.EndSameAsBegin,
            BeginGuard = mode.BeginGuard,
            EndScope = mode.EndScope,
            KeywordValidator = mode.KeywordValidator,
            Skip = mode.Skip,
        };
        memo[mode] = cmode;

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
        var relevanceZero = false;

        // beginKeywords sugar — `(?<!\.)` mirrors hljs's skipIfHasPrecedingDot,
        // which prevents matches like `foo.catch(` from being treated as a keyword.
        if (mode.BeginKeywords is { Count: > 0 } beginKeywords)
        {
            begin = @"(?<!\.)\b(" + string.Join('|', beginKeywords.Select(Regex.Escape)) + @")(?!\.)(?=\b|\s)";
            keywords ??= Keywords.FromWords(beginKeywords);
            relevanceZero = true;
        }

        // Defaults for child modes when neither begin nor end is set
        if (parent is not null)
        {
            if (string.IsNullOrEmpty(begin))
                begin = @"\B|\b";
            if (string.IsNullOrEmpty(end) && !mode.EndsWithParent)
                end = @"\B|\b";
        }

        var options = RegexOptions.Multiline | RegexOptions.CultureInvariant;
        if (caseInsensitive)
            options |= RegexOptions.IgnoreCase;

        if (!string.IsNullOrEmpty(begin))
            cmode.BeginRe = new Regex(begin, options, RegexTimeout);
        if (!string.IsNullOrEmpty(end))
            cmode.EndRe = new Regex(end, options, RegexTimeout);
        if (!string.IsNullOrEmpty(mode.Illegal))
            cmode.IllegalRe = new Regex(mode.Illegal, options, RegexTimeout);

        if (keywords is not null)
        {
            cmode.KeywordPatternRe = new Regex(mode.KeywordPattern ?? @"\w+", options, RegexTimeout);
            cmode.KeywordMap = BuildKeywordMap(keywords, caseInsensitive);
        }

        // Compile children, expanding 'self' and 'variants' as we go
        foreach (var raw in mode.Contains)
        {
            foreach (var expanded in Expand(raw, mode, expandCache))
            {
                var child = CompileMode(expanded, cmode, caseInsensitive, aliases, memo, expandCache);
                cmode.Contains.Add(child);
            }
        }

        if (mode.Starts is not null)
            cmode.Starts = CompileMode(mode.Starts, parent, caseInsensitive, aliases, memo, expandCache);

        _ = relevanceZero; // relevance is not used; we don't emit relevance information
        return cmode;
    }

    private static IReadOnlyList<Mode> Expand(Mode mode, Mode enclosing, Dictionary<Mode, IReadOnlyList<Mode>> expandCache)
    {
        if (ReferenceEquals(mode, Mode.Self))
            return [enclosing];

        // Cache the expanded variants per source Mode so that recursive grammars
        // (e.g. Razor, where `m13` and `m11` reference each other) produce the
        // same expanded instances on every visit, allowing the CompileMode memo
        // to short-circuit cycles.
        if (expandCache.TryGetValue(mode, out var cached))
            return cached;

        IReadOnlyList<Mode> result;
        if (mode.Variants is { Count: > 0 } variants)
        {
            var list = new List<Mode>(variants.Count);
            foreach (var v in variants)
            {
                list.Add(mode.With(b =>
                {
                    b.Variants = null;
                    if (v.ClearScope)
                        b.Scope = null;
                    else
                        b.Scope = v.Scope ?? b.Scope;
                    b.Match = v.Match ?? b.Match;
                    b.Begin = v.Begin ?? b.Begin;
                    b.End = v.End ?? b.End;
                    b.EndScope = v.EndScope ?? b.EndScope;
                    b.BeginParts = v.BeginParts ?? b.BeginParts;
                    b.BeginScope = v.BeginScope ?? b.BeginScope;
                    b.BeginGuard = v.BeginGuard ?? b.BeginGuard;
                    b.SubLanguage = v.SubLanguage ?? b.SubLanguage;
                    b.BeginKeywords = v.BeginKeywords ?? b.BeginKeywords;
                    b.Illegal = v.Illegal ?? b.Illegal;
                    b.Keywords = v.Keywords ?? b.Keywords;
                    b.KeywordPattern = v.KeywordPattern ?? b.KeywordPattern;
                    b.KeywordValidator = v.KeywordValidator ?? b.KeywordValidator;
                    if (v.Contains.Count > 0)
                        b.Contains = v.Contains;
                    if (v.Starts is not null)
                        b.Starts = v.Starts;
                    if (v.ExcludeBegin)
                        b.ExcludeBegin = true;
                    if (v.ExcludeEnd)
                        b.ExcludeEnd = true;
                    if (v.ReturnBegin)
                        b.ReturnBegin = true;
                    if (v.ReturnEnd)
                        b.ReturnEnd = true;
                    if (v.EndsWithParent)
                        b.EndsWithParent = true;
                    if (v.EndsParent)
                        b.EndsParent = true;
                    if (v.EndSameAsBegin)
                        b.EndSameAsBegin = true;
                    if (v.Skip)
                        b.Skip = true;
                }));
            }
            result = list;
        }
        else
        {
            result = [mode];
        }

        expandCache[mode] = result;
        return result;
    }

    private static int CountCapturingGroups(string pattern)
    {
        try
        {
            var re = new Regex(pattern, RegexOptions.None, RegexTimeout);
            // GetGroupNumbers returns all group numbers including 0; subtract 1 for the implicit whole-match group.
            // Then subtract named groups (these are also counted but they have explicit names).
            var total = re.GetGroupNumbers().Length - 1;
            var names = re.GetGroupNames().Count(n => !int.TryParse(n, CultureInfo.InvariantCulture, out _));
            return Math.Max(0, total - names);
        }
        catch
        {
            return 0;
        }
    }

    private static Dictionary<string, KeywordHit> BuildKeywordMap(Keywords keywords, bool caseInsensitive)
    {
        var map = new Dictionary<string, KeywordHit>(caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var (scope, words) in keywords.Groups)
        {
            var keywordScope = scope is "_" ? null : scope;

            foreach (var raw in words)
            {
                var split = raw.Split('|');
                var word = split[0];
                var relevance = split.Length > 1 && int.TryParse(split[1], CultureInfo.InvariantCulture, out var r) ? r : (CommonKeywords.Contains(word) ? 0 : 1);
                map[word] = new KeywordHit(keywordScope, relevance);
            }
        }

        return map;
    }
}
