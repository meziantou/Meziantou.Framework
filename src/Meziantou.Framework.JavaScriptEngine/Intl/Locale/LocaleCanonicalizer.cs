using Meziantou.Framework.JavaScriptEngine.Intl.Data;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>
/// The alias replacements of UTS 35 (Annex C, LocaleId Canonicalization) from the supplemental data of CLDR: the
/// languageAlias, territoryAlias, scriptAlias and variantAlias rules for the language ids, the subdivisionAlias rules for
/// the "rg" and "sd" keywords, and the aliases of the BCP 47 data (bcp47/*.json) for the values of the keywords and of
/// the tfields.
/// </summary>
internal static class LocaleCanonicalizer
{
    private static Tables? s_tables;

    private static Tables Data => Volatile.Read(ref s_tables) ?? CldrData.InitializeOnce(ref s_tables, new Tables(CldrData.Default.Supplemental));

    /// <summary>Replaces the aliases of a language id (or of a tlang, whose subtags are in lower case) and sorts its variants.</summary>
    public static void CanonicalizeLanguageId(LanguageTag tag, bool transformed)
    {
        if (transformed)
        {
            tag.Script = LanguageTag.ToTitle(tag.Script);
            tag.Region = tag.Region.ToUpperInvariant();
        }

        tag.Variants.Sort(StringComparer.Ordinal);
        var data = Data;
        for (var iteration = 0; iteration < 32; iteration++)
        {
            if (data.TryFindLanguageRule(tag, out var rule))
            {
                rule.Apply(tag);
                continue;
            }

            if (tag.Region.Length > 0 && data.TerritoryAliases.TryGetValue(tag.Region, out var regions))
            {
                tag.Region = SelectRegion(tag, regions);
                continue;
            }

            if (tag.Script.Length > 0 && data.ScriptAliases.TryGetValue(tag.Script, out var script))
            {
                tag.Script = script;
                continue;
            }

            var changed = false;
            for (var i = 0; i < tag.Variants.Count; i++)
            {
                if (data.VariantAliases.TryGetValue(tag.Variants[i], out var variant))
                {
                    tag.Variants.RemoveAt(i);
                    if (!tag.Variants.Contains(variant))
                    {
                        tag.Variants.Add(variant);
                    }

                    tag.Variants.Sort(StringComparer.Ordinal);
                    changed = true;
                    break;
                }
            }

            if (!changed)
                break;
        }

        if (transformed)
        {
            tag.Script = tag.Script.ToLowerInvariant();
            tag.Region = tag.Region.ToLowerInvariant();
        }
    }

    // UTS 35: with several replacements, the region of the likely subtags of the language (and script) when it is one of
    // them, else the first one
    private static string SelectRegion(LanguageTag tag, string[] regions)
    {
        if (regions.Length == 1)
            return regions[0];

        var likely = CldrData.Default.Supplemental.LikelySubtags;
        if (likely.TryAddLikelySubtags(new LanguageId(tag.Language, tag.Script, ""), out var maximized) && Array.IndexOf(regions, maximized.Region) >= 0)
            return maximized.Region;

        return regions[0];
    }

    /// <summary>
    /// CanonicalizeUValue(key, value): the canonical value of a keyword of the Unicode locale extension (lower case,
    /// aliases replaced); the value "true" is the empty string (UTS 35 removes it).
    /// </summary>
    public static string CanonicalizeUnicodeValue(string key, string value)
    {
        value = CanonicalizeUnicodeValueCore(key, value.ToLowerInvariant());
        return value is "true" ? "" : value;
    }

    private static string CanonicalizeUnicodeValueCore(string key, string value)
    {
        var data = Data;
        if (key is "rg" or "sd")
        {
            if (data.SubdivisionAliases.TryGetValue(value, out var replacement))
                return replacement;

            return value;
        }

        if (data.UnicodeValueAliases.TryGetValue(key, out var aliases) && aliases.TryGetValue(value, out var canonical))
            return canonical;

        return value;
    }

    /// <summary>
    /// Gets the BCP 47 type of a keyword from its CLDR name (the "_alias" of the BCP 47 data, e.g. ("co", "phonebook") ->
    /// "phonebk"), or the name itself.
    /// </summary>
    public static string GetBcp47Type(string key, string name)
    {
        if (Data.LegacyTypeNames.TryGetValue(key, out var names) && names.TryGetValue(name, out var type))
            return type;

        return name;
    }

    /// <summary>Gets the canonical value of a tfield of the transformed extension (lower case, aliases replaced).</summary>
    public static string CanonicalizeTransformedValue(string key, string value)
    {
        value = value.ToLowerInvariant();
        if (Data.TransformedValueAliases.TryGetValue(key, out var aliases) && aliases.TryGetValue(value, out var canonical))
            return canonical;

        return value;
    }

    private sealed class LanguageRule
    {
        public required string Language { get; init; }

        public required string Script { get; init; }

        public required string Region { get; init; }

        public required string[] Variants { get; init; }

        public required string ReplacementLanguage { get; init; }

        public required string ReplacementScript { get; init; }

        public required string ReplacementRegion { get; init; }

        public required string[] ReplacementVariants { get; init; }

        public bool Matches(LanguageTag tag)
        {
            if (Language is not "und" && Language != tag.Language)
                return false;

            if (Script.Length > 0 && Script != tag.Script)
                return false;

            if (Region.Length > 0 && Region != tag.Region)
                return false;

            foreach (var variant in Variants)
            {
                if (!tag.Variants.Contains(variant))
                    return false;
            }

            return true;
        }

        // Ordered by the number of variants, then the region, the script and the language of the type
        public int Priority => (Variants.Length * 8) + (Region.Length > 0 ? 4 : 0) + (Script.Length > 0 ? 2 : 0) + (Language is "und" ? 0 : 1);

        public void Apply(LanguageTag tag)
        {
            if (Language is not "und" || tag.Language is "und")
            {
                tag.Language = ReplacementLanguage;
            }

            if (Script.Length > 0 || tag.Script.Length == 0)
            {
                tag.Script = ReplacementScript;
            }

            if (Region.Length > 0 || tag.Region.Length == 0)
            {
                tag.Region = ReplacementRegion;
            }

            foreach (var variant in Variants)
            {
                tag.Variants.Remove(variant);
            }

            foreach (var variant in ReplacementVariants)
            {
                if (!tag.Variants.Contains(variant))
                {
                    tag.Variants.Add(variant);
                }
            }

            tag.Variants.Sort(StringComparer.Ordinal);
        }
    }

    private sealed class Tables
    {
        private readonly Dictionary<string, List<LanguageRule>> _languageRules = new(StringComparer.Ordinal);

        public Tables(CldrSupplemental supplemental)
        {
            var aliases = supplemental.GetSection("aliases") ?? throw new InvalidDataException("The aliases are missing");
            foreach (var (key, value) in Entries(aliases, "languageAlias"))
            {
                if (!TryParseLanguageId(key, out var type) || !TryParseLanguageId(value, out var replacement))
                    continue;

                var rule = new LanguageRule
                {
                    Language = type.Language,
                    Script = type.Script,
                    Region = type.Region,
                    Variants = type.Variants,
                    ReplacementLanguage = replacement.Language,
                    ReplacementScript = replacement.Script,
                    ReplacementRegion = replacement.Region,
                    ReplacementVariants = replacement.Variants,
                };

                if (!_languageRules.TryGetValue(rule.Language, out var rules))
                {
                    rules = [];
                    _languageRules.Add(rule.Language, rules);
                }

                rules.Add(rule);
            }

            foreach (var rules in _languageRules.Values)
            {
                rules.Sort(static (a, b) => b.Priority.CompareTo(a.Priority));
            }

            foreach (var (key, value) in Entries(aliases, "territoryAlias"))
            {
                TerritoryAliases[key] = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            }

            foreach (var (key, value) in Entries(aliases, "scriptAlias"))
            {
                ScriptAliases[key] = value;
            }

            foreach (var (key, value) in Entries(aliases, "variantAlias"))
            {
                VariantAliases[key.ToLowerInvariant()] = value.ToLowerInvariant();
            }

            foreach (var (key, value) in Entries(aliases, "subdivisionAlias"))
            {
                var replacement = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

                // A subdivision replaced by a region: the value of a region in "rg" and "sd" is the region and "zzzz"
                if (LanguageTag.IsRegionSubtag(replacement) && char.IsAsciiLetterUpper(replacement[0]))
                {
                    replacement += "zzzz";
                }

                SubdivisionAliases[key.ToLowerInvariant()] = replacement.ToLowerInvariant();
            }

            if (supplemental.GetSection("bcp47") is { } bcp47)
            {
                ReadValueAliases(bcp47, "u", UnicodeValueAliases, LegacyTypeNames);
                ReadValueAliases(bcp47, "t", TransformedValueAliases, legacyNames: null);
            }
        }

        public Dictionary<string, string[]> TerritoryAliases { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> ScriptAliases { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> VariantAliases { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> SubdivisionAliases { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Dictionary<string, string>> UnicodeValueAliases { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Dictionary<string, string>> TransformedValueAliases { get; } = new(StringComparer.Ordinal);

        /// <summary>Gets the BCP 47 types of the CLDR names of the keywords ("_alias", e.g. co: phonebook -> phonebk).</summary>
        public Dictionary<string, Dictionary<string, string>> LegacyTypeNames { get; } = new(StringComparer.Ordinal);

        public bool TryFindLanguageRule(LanguageTag tag, [NotNullWhen(true)] out LanguageRule? result)
        {
            result = null;
            if (_languageRules.TryGetValue(tag.Language, out var rules))
            {
                foreach (var rule in rules)
                {
                    if (rule.Matches(tag))
                    {
                        result = rule;
                        break;
                    }
                }
            }

            if (_languageRules.TryGetValue("und", out var undRules))
            {
                foreach (var rule in undRules)
                {
                    if ((result is null || rule.Priority > result.Priority) && rule.Matches(tag))
                    {
                        result = rule;
                        break;
                    }
                }
            }

            return result is not null;
        }

        private static List<KeyValuePair<string, string>> Entries(CldrTable aliases, string name)
        {
            var result = new List<KeyValuePair<string, string>>();
            if (aliases.TryGetNode(name, out var node) && node.IsTable)
            {
                var table = node.GetTable();
                for (var i = 0; i < table.Count; i++)
                {
                    var value = table.GetValue(i);
                    if (value.IsString)
                    {
                        result.Add(new(table.GetKey(i), value.GetString()));
                    }
                }
            }

            return result;
        }

        // bcp47/u/ca/{ islamicc: { _preferred: "islamic-civil" }, ethioaa: { _alias: "ethiopic-amete-alem" } }
        private static void ReadValueAliases(CldrTable bcp47, string extension, Dictionary<string, Dictionary<string, string>> result, Dictionary<string, Dictionary<string, string>>? legacyNames)
        {
            if (!bcp47.TryGetNode(extension, out var node) || !node.IsTable)
                return;

            var keys = node.GetTable();
            for (var i = 0; i < keys.Count; i++)
            {
                var key = keys.GetKey(i);
                if (key.StartsWith('_', StringComparison.Ordinal) || !keys.GetValue(i).IsTable)
                    continue;

                var types = keys.GetValue(i).GetTable();
                var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
                var names = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var j = 0; j < types.Count; j++)
                {
                    var type = types.GetKey(j);
                    if (type.StartsWith('_', StringComparison.Ordinal) || !types.GetValue(j).IsTable)
                        continue;

                    var info = types.GetValue(j).GetTable();
                    var canonical = type;
                    if (info.TryGetString("_preferred", out var preferred) && LanguageTag.IsUnicodeType(preferred))
                    {
                        canonical = preferred;
                        aliases[type] = preferred;
                    }

                    if (info.TryGetString("_alias", out var alias))
                    {
                        foreach (var item in alias.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        {
                            names.TryAdd(item, canonical);
                            var lower = item.ToLowerInvariant();
                            if (lower != canonical && lower != type && LanguageTag.IsUnicodeType(lower))
                            {
                                aliases.TryAdd(lower, canonical);
                            }
                        }
                    }
                }

                if (aliases.Count > 0)
                {
                    result[key] = aliases;
                }

                if (legacyNames is not null && names.Count > 0)
                {
                    legacyNames[key] = names;
                }
            }
        }

        private static bool TryParseLanguageId(string value, out (string Language, string Script, string Region, string[] Variants) result)
        {
            result = default;
            var subtags = value.Split('-');
            var index = 0;
            if (!LanguageTag.IsLanguageSubtag(subtags[0]) && subtags[0] is not "und")
                return false;

            var language = subtags[index++].ToLowerInvariant();
            var script = "";
            var region = "";
            if (index < subtags.Length && LanguageTag.IsScriptSubtag(subtags[index]))
            {
                script = LanguageTag.ToTitle(subtags[index++]);
            }

            if (index < subtags.Length && LanguageTag.IsRegionSubtag(subtags[index]))
            {
                region = subtags[index++].ToUpperInvariant();
            }

            var variants = new List<string>();
            while (index < subtags.Length && LanguageTag.IsVariantSubtag(subtags[index]))
            {
                variants.Add(subtags[index++].ToLowerInvariant());
            }

            if (index != subtags.Length)
                return false;

            result = (language, script, region, [.. variants]);
            return true;
        }
    }
}
