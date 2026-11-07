using Meziantou.Framework.JavaScriptEngine.Intl.Data;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>
/// The display names of a locale (CLDR localeDisplayNames, currencies and dateFields), looked up as ICU's
/// LocaleDisplayNames does: the short style uses the "-alt-short" names when they exist (the narrow style too).
/// </summary>
internal readonly struct DisplayNamesData(CldrLocale locale, bool isShort)
{
    /// <summary>
    /// The display name of a canonical unicode_language_id (UTS 35 "Locale Display Name Algorithm"), or null when a
    /// subtag has no name and <paramref name="substitute"/> is false (else the code of the subtag is used).
    /// </summary>
    /// <remarks>
    /// With the dialect names, the name of the language with its script and region is used when CLDR has one (e.g.
    /// "en-GB" is "British English"). As ICU, a tag with both a script and a region uses only the name of the whole
    /// language-script-region, not the one of language-script or language-region ("zh-Hant-TW" is "Chinese
    /// (Traditional, Taiwan)"). Each variant is displayed with its own name (ICU displays several variants as one
    /// unknown variant, e.g. "FONIPA_VALENCIA").
    /// </remarks>
    public string? GetLocaleDisplayName(string code, bool dialect, bool substitute)
    {
        var tag = LanguageTag.Parse(code)!;
        var language = tag.Language;
        var hasScript = tag.Script.Length > 0;
        var hasRegion = tag.Region.Length > 0;
        string? name = null;
        if (dialect)
        {
            if (hasScript && hasRegion)
            {
                name = GetLanguageName(language + "-" + tag.Script + "-" + tag.Region);
                if (name is not null)
                {
                    hasScript = false;
                    hasRegion = false;
                }
            }
            else if (hasScript)
            {
                name = GetLanguageName(language + "-" + tag.Script);
                if (name is not null)
                {
                    hasScript = false;
                }
            }
            else if (hasRegion)
            {
                name = GetLanguageName(language + "-" + tag.Region);
                if (name is not null)
                {
                    hasRegion = false;
                }
            }
        }

        name ??= GetLanguageName(language);
        if (name is null)
        {
            if (!substitute)
                return null;

            name = language;
        }

        string? remainder = null;
        if (hasScript)
        {
            var scriptName = GetScriptName(tag.Script);
            if (scriptName is null && !substitute)
                return null;

            remainder = AppendWithSeparator(remainder, scriptName ?? tag.Script);
        }

        if (hasRegion)
        {
            var regionName = GetRegionName(tag.Region);
            if (regionName is null && !substitute)
                return null;

            remainder = AppendWithSeparator(remainder, regionName ?? tag.Region);
        }

        foreach (var variant in tag.Variants)
        {
            var upper = variant.ToUpperInvariant();
            var variantName = locale.GetString(CldrCategory.DisplayNames, "variants/" + upper);
            if (variantName is null && !substitute)
                return null;

            remainder = AppendWithSeparator(remainder, variantName ?? upper);
        }

        if (remainder is null)
            return name;

        // The parentheses of the names in the parentheses of the pattern become brackets (as ICU)
        var pattern = locale.GetString(CldrCategory.DisplayNames, "localeDisplayPattern/localePattern") ?? "{0} ({1})";
        if (pattern.Contains('\uFF08', StringComparison.Ordinal))
        {
            remainder = remainder.Replace('\uFF08', '\uFF3B').Replace('\uFF09', '\uFF3D');
        }
        else
        {
            remainder = remainder.Replace('(', '[').Replace(')', ']');
        }

        return Format(pattern, name, remainder);
    }

    private string AppendWithSeparator(string? buffer, string value)
    {
        if (buffer is null)
            return value;

        var separator = locale.GetString(CldrCategory.DisplayNames, "localeDisplayPattern/localeSeparator") ?? "{0}, {1}";
        return Format(separator, buffer, value);
    }

    private static string Format(string pattern, string first, string second)
    {
        return pattern.Replace("{0}", first, StringComparison.Ordinal).Replace("{1}", second, StringComparison.Ordinal);
    }

    /// <summary>The name of a language (the key of CLDR languages, e.g. "en", "en-GB", "zh-Hant").</summary>
    private string? GetLanguageName(string key) => GetName("languages/", key);

    /// <summary>The name of a script (title case, e.g. "Latn").</summary>
    public string? GetScriptName(string code) => GetName("scripts/", code);

    /// <summary>The name of a region (upper case, e.g. "GB", or 3 digits); the deprecated codes that ICU replaces (e.g. "UK", "SU") use the name of their replacement.</summary>
    public string? GetRegionName(string code)
    {
        if (GetName("territories/", code) is { } name)
            return name;

        var replacement = code switch
        {
            "AN" => "CW",
            "BU" => "MM",
            "CS" => "RS",
            "DD" => "DE",
            "DY" => "BJ",
            "FX" => "FR",
            "HV" => "BF",
            "NH" => "VU",
            "RH" => "ZW",
            "SU" => "RU",
            "TP" => "TL",
            "UK" => "GB",
            "VD" => "VN",
            "YD" => "YE",
            "YU" => "RS",
            "ZR" => "CD",
            _ => null,
        };

        // As ICU, the replacement is looked up in the long names only
        return replacement is null ? null : locale.GetString(CldrCategory.DisplayNames, "territories/" + replacement);
    }

    /// <summary>The name of a currency (upper case code, e.g. "EUR"): its displayName in every style, as ICU.</summary>
    public string? GetCurrencyName(string code) => locale.GetString(CldrCategory.Currencies, code + "/displayName");

    /// <summary>The name of a calendar (a canonical BCP 47 calendar, e.g. "gregory").</summary>
    public string? GetCalendarName(string code)
    {
        var type = code switch
        {
            "gregory" => "gregorian",
            "ethioaa" => "ethiopic-amete-alem",
            _ => code,
        };

        return GetName("types/calendar/", type);
    }

    /// <summary>The name of a date-time field (IsValidDateTimeFieldCode), from the display names of CLDR dateFields.</summary>
    public string? GetDateTimeFieldName(string code, string style)
    {
        var field = code switch
        {
            "weekOfYear" => "week",
            "dayPeriod" => "dayperiod",
            "timeZoneName" => "zone",
            _ => code,
        };

        if (style is not "long")
        {
            field += "-" + style;
        }

        return locale.GetString(CldrCategory.Fields, field + "/displayName");
    }

    private string? GetName(string prefix, string key)
    {
        if (isShort && locale.GetString(CldrCategory.DisplayNames, prefix + key + "-alt-short") is { } shortName)
            return shortName;

        return locale.GetString(CldrCategory.DisplayNames, prefix + key);
    }
}
