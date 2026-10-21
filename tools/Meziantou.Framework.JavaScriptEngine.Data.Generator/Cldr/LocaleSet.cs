using Meziantou.Framework.JavaScriptEngine.Intl.Data;

namespace Meziantou.Framework.JavaScriptEngine.Data.Generator.Cldr;

/// <summary>
/// The locales embedded in the engine: the locales of CLDR coverage modern and moderate (cldr-core coverageLevels.json),
/// their parents (parentLocales.json and the inheritance rules of UTS 35), and their aliases (the default content locales,
/// e.g. "fr-FR" -> "fr", and the language-region identifiers whose likely script is not the default one of the language,
/// e.g. "zh-TW" -> "zh-Hant-TW" -> "zh-Hant").
/// </summary>
internal sealed class LocaleSet
{
    public const string Root = "und";

    private readonly Dictionary<string, string> _explicitParents = new(StringComparer.Ordinal);
    private readonly LikelySubtags _likelySubtags;
    private readonly HashSet<string> _available;

    public LocaleSet(CldrJsonSource source, LikelySubtags likelySubtags)
    {
        _likelySubtags = likelySubtags;
        using (var parents = source.Read("cldr-core/supplemental/parentLocales.json"))
        {
            foreach (var property in parents.RootElement.GetProperty("supplemental").GetProperty("parentLocales").GetProperty("parentLocale").EnumerateObject())
            {
                var parent = property.Value.GetString()!;
                _explicitParents.Add(property.Name, parent is "root" ? Root : parent);
            }
        }

        var available = source.GetMainLocales("cldr-numbers-full").ToHashSet(StringComparer.Ordinal);
        _available = available;
        var locales = new SortedSet<string>(StringComparer.Ordinal) { Root };
        using (var coverage = source.Read("cldr-core/coverageLevels.json"))
        {
            foreach (var property in coverage.RootElement.GetProperty("effectiveCoverageLevels").EnumerateObject())
            {
                if (property.Value.GetString() is "modern" or "moderate")
                {
                    for (var locale = property.Name; locale is not null; locale = GetParent(locale))
                    {
                        if (!available.Contains(locale))
                            throw new InvalidDataException("The locale has no data in cldr-json: " + locale);

                        locales.Add(locale);
                    }
                }
            }
        }

        Locales = [.. locales];

        // Default content locales: they have the data of their parent
        var aliases = new SortedDictionary<string, string>(StringComparer.Ordinal);
        using (var defaultContent = source.Read("cldr-core/defaultContent.json"))
        {
            var defaultContentLocales = defaultContent.RootElement.GetProperty("defaultContent").EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal);
            foreach (var locale in defaultContentLocales.Order(StringComparer.Ordinal))
            {
                if (locales.Contains(locale))
                    continue;

                var target = GetParent(locale);
                while (target is not null && !locales.Contains(target) && defaultContentLocales.Contains(target))
                {
                    target = GetParent(target);
                }

                if (target is not null and not Root && locales.Contains(target))
                {
                    aliases.Add(locale, target);
                }
            }
        }

        // language-region identifiers whose likely script differs from the default script of the language
        foreach (var name in locales.Concat(aliases.Keys).ToList())
        {
            var id = LanguageId.Parse(name);
            if (!id.HasScript || !id.HasRegion || id.HasVariants)
                continue;

            var alias = new LanguageId(id.Language, "", id.Region).ToString();
            if (locales.Contains(alias) || aliases.ContainsKey(alias))
                continue;

            if (_likelySubtags.TryAddLikelySubtags(LanguageId.Parse(alias), out var maximized) && maximized == id)
            {
                aliases.Add(alias, aliases.TryGetValue(name, out var target) ? target : name);
            }
        }

        Aliases = aliases;
    }

    /// <summary>Gets the locales, sorted by ordinal, with the root ("und").</summary>
    public IReadOnlyList<string> Locales { get; }

    public IReadOnlyDictionary<string, string> Aliases { get; }

    /// <summary>
    /// Gets the parent of a locale (UTS 35 "Parent Locales"), or null for the root. The parents that have no data in
    /// cldr-json are default content locales (e.g. "ca-ES", the parent of "ca-ES-valencia"): their own parent is used.
    /// </summary>
    public string? GetParent(string locale)
    {
        var parent = GetCldrParent(locale);
        while (parent is not null && !_available.Contains(parent) && parent is not Root)
        {
            parent = GetCldrParent(parent);
        }

        return parent;
    }

    private string? GetCldrParent(string locale)
    {
        if (locale is Root)
            return null;

        if (_explicitParents.TryGetValue(locale, out var parent))
            return parent;

        var subtags = locale.Split('-');
        if (subtags.Length == 1)
            return Root;

        // nonlikelyScript: the parent of a language-script locale whose script is not the likely one is the root
        if (subtags.Length == 2 && subtags[1].Length == 4 && _likelySubtags.TryAddLikelySubtags(new LanguageId(subtags[0], "", ""), out var maximized) && !string.Equals(maximized.Script, subtags[1], StringComparison.Ordinal))
            return Root;

        return string.Join('-', subtags[..^1]);
    }

    /// <summary>Gets the group of a locale: its language, whose data is stored in the same chunks.</summary>
    public static string GetGroup(string locale) => locale.Split('-')[0];
}
