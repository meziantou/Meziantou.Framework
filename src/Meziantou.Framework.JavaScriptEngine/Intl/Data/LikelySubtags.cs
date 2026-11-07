namespace Meziantou.Framework.JavaScriptEngine.Intl.Data;

/// <summary>
/// The likely subtags of CLDR (supplemental likelySubtags.json) and the Add Likely Subtags and Remove Likely Subtags
/// algorithms of UTS 35 (section 4.4). The input must already be canonicalized (aliases replaced).
/// </summary>
internal sealed class LikelySubtags
{
    private readonly CldrTable _table;

    internal LikelySubtags(CldrTable table) => _table = table;

    public int Count => _table.Count;

    /// <summary>Gets the value of an entry of the table (e.g. "und-TW" \u2192 "zh-Hant-TW").</summary>
    public bool TryGetEntry(string key, [NotNullWhen(true)] out string? value) => _table.TryGetString(key, out value);

    /// <summary>
    /// Add Likely Subtags: returns false when no entry matches (an unknown language such as "qaa", even with a script
    /// and a region, as the test data of CLDR expects).
    /// </summary>
    public bool TryAddLikelySubtags(LanguageId source, out LanguageId result)
    {
        // The script "Zzzz" and the region "ZZ" are empty
        if (source.Script is "Zzzz")
        {
            source = source.WithScript("");
        }

        if (source.Region is "ZZ")
        {
            source = source.WithRegion("");
        }

        if (TryLookup(source, out var match))
        {
            result = new LanguageId(
                source.IsUnknownLanguage ? match.Language : source.Language,
                source.HasScript ? source.Script : match.Script,
                source.HasRegion ? source.Region : match.Region,
                source.Variants);
            return true;
        }

        result = source;
        return false;
    }

    /// <summary>Add Likely Subtags, returning the source unchanged when it fails.</summary>
    public LanguageId AddLikelySubtags(LanguageId source) => TryAddLikelySubtags(source, out var result) ? result : source;

    /// <summary>Remove Likely Subtags. When <paramref name="favorRegion"/> is true, "zh-TW" is preferred to "zh-Hant".</summary>
    public LanguageId RemoveLikelySubtags(LanguageId source, bool favorRegion = false)
    {
        if (!TryAddLikelySubtags(source.WithoutVariants(), out var max))
            return source;

        var language = new LanguageId(max.Language, "", "");
        if (Matches(language, max))
            return language with { Variants = source.Variants };

        var first = favorRegion ? language.WithRegion(max.Region) : language.WithScript(max.Script);
        if (Matches(first, max))
            return first with { Variants = source.Variants };

        var second = favorRegion ? language.WithScript(max.Script) : language.WithRegion(max.Region);
        if (Matches(second, max))
            return second with { Variants = source.Variants };

        return max with { Variants = source.Variants };

        bool Matches(LanguageId trial, LanguageId expected) => TryAddLikelySubtags(trial, out var maximized) && maximized == expected;
    }

    private bool TryLookup(LanguageId source, out LanguageId match)
    {
        if (source.HasScript && source.HasRegion && TryGet(source.Language + "-" + source.Script + "-" + source.Region, out match))
            return true;

        if (source.HasScript && TryGet(source.Language + "-" + source.Script, out match))
            return true;

        if (source.HasRegion && TryGet(source.Language + "-" + source.Region, out match))
            return true;

        if (TryGet(source.Language, out match))
            return true;

        match = default;
        return false;
    }

    private bool TryGet(string key, out LanguageId value)
    {
        if (_table.TryGetString(key, out var text))
        {
            value = LanguageId.Parse(text);
            return true;
        }

        value = default;
        return false;
    }
}
