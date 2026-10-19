using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The OpenType script tags of the Unicode scripts (https://learn.microsoft.com/typography/opentype/spec/scripttags), as
/// HarfBuzz's <c>hb_ot_all_tags_from_script</c> (hb-ot-tag.cc, MIT license): the tags of the Indic scripts of the OpenType
/// shaping specifications version 3 and 2 (<c>dev3</c>, <c>dev2</c>), then the old tag (<c>deva</c>); the tag of the other
/// scripts is their ISO 15924 code in lowercase, except a few ones.
/// </summary>
internal static class OpenTypeScriptTags
{
    /// <summary>Writes the tags of a script in order of preference; returns their number (at most 3).</summary>
    public static int GetTags(UnicodeScript script, Span<uint> tags)
    {
        var count = 0;
        var newTag = GetNewTag(script);
        if (newTag != 0)
        {
            // Myanmar maps to mym2, but there is no mym3
            if (script != UnicodeScript.Myanmar)
            {
                tags[count++] = (newTag & 0xFFFFFF00) | '3';
            }

            tags[count++] = newTag;
        }

        var oldTag = GetOldTag(script);
        if (oldTag != 0)
        {
            tags[count++] = oldTag;
        }

        return count;
    }

    /// <summary>The tag of the OpenType shaping specifications version 2 of the Indic scripts and Myanmar, or 0.</summary>
    private static uint GetNewTag(UnicodeScript script) => script switch
    {
        UnicodeScript.Bengali => 0x626E6732, // bng2
        UnicodeScript.Devanagari => 0x64657632, // dev2
        UnicodeScript.Gujarati => 0x676A7232, // gjr2
        UnicodeScript.Gurmukhi => 0x67757232, // gur2
        UnicodeScript.Kannada => 0x6B6E6432, // knd2
        UnicodeScript.Malayalam => 0x6D6C6D32, // mlm2
        UnicodeScript.Oriya => 0x6F727932, // ory2
        UnicodeScript.Tamil => 0x746D6C32, // tml2
        UnicodeScript.Telugu => 0x74656C32, // tel2
        UnicodeScript.Myanmar => 0x6D796D32, // mym2
        _ => 0,
    };

    /// <summary>The old tag of a script: the ISO 15924 code with a lowercase first letter, except a few scripts.</summary>
    private static uint GetOldTag(UnicodeScript script)
    {
        switch (script)
        {
            case UnicodeScript.Unknown:
                return 0;

            // Hiragana and Katakana map to kana
            case UnicodeScript.Hiragana or UnicodeScript.Katakana or UnicodeScript.KatakanaOrHiragana:
                return 0x6B616E61; // kana

            // The spaces at the end are kept, unlike ISO 15924
            case UnicodeScript.Lao:
                return 0x6C616F20; // lao
            case UnicodeScript.Yi:
                return 0x79692020; // yi
            case UnicodeScript.Nko:
                return 0x6E6B6F20; // nko
            case UnicodeScript.Vai:
                return 0x76616920; // vai
            default:
                // The ISO 15924 code, with a lowercase first letter (e.g. Latn to latn)
                var code = UnicodeScripts.GetIso15924Code(script);
                return ((uint)code[0] << 24 | (uint)code[1] << 16 | (uint)code[2] << 8 | code[3]) | 0x20000000;
        }
    }
}

/// <summary>
/// The OpenType language system tags of a BCP 47 language tag (https://learn.microsoft.com/typography/opentype/spec/languagetags),
/// as HarfBuzz's <c>hb_ot_tags_from_language</c> (hb-ot-tag.cc, MIT license), from its table (<c>OpenTypeLanguageTags.g.cs</c>):
/// the rules of the tags with several subtags (e.g. <c>zh-Hant-HK</c>), then the tags of the ISO 639 language (or of the
/// extended language subtag), else the ISO 639-3 code in uppercase.
/// </summary>
internal static partial class OpenTypeLanguageTags
{
    /// <summary>Returns the tags of a BCP 47 language tag in order of preference (empty for null or an unknown tag).</summary>
    public static uint[] GetTags(string? language)
    {
        if (string.IsNullOrEmpty(language))
            return [];

        var tag = language.ToLowerInvariant().Replace('_', '-');

        // The private use subtags are ignored
        var limit = tag.Length;
        if (tag.StartsWith("x-", StringComparison.Ordinal))
            return [];

        var privateUse = tag.IndexOf("-x-", StringComparison.Ordinal);
        if (privateUse >= 0)
        {
            limit = privateUse;
        }

        if (FromComplexLanguage(tag, limit) is { } complex)
            return complex;

        // The first component, or the extended language subtag
        var start = 0;
        var dash = tag.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0 && limit >= 6)
        {
            var extendedEnd = tag.IndexOf('-', dash + 1, StringComparison.Ordinal);
            var extendedLength = (extendedEnd >= 0 ? extendedEnd : tag.Length) - dash - 1;
            if (extendedLength == 3 && char.IsAsciiLetter(tag[dash + 1]))
            {
                start = dash + 1;
            }
        }

        var firstDash = tag.IndexOf('-', start, StringComparison.Ordinal);
        var firstLength = (firstDash >= 0 && firstDash < limit ? firstDash : limit) - start;
        if (firstLength is < 2 or > 4)
            return [];

        var key = ToTag(tag.AsSpan(start, firstLength));
        if (firstLength == 2)
        {
            var keys = Languages2Keys;
            var index = keys.BinarySearch(key);
            if (index < 0)
                return [];

            while (index > 0 && keys[index - 1] == key)
            {
                index--;
            }

            var result = new List<uint>();
            for (var i = index; i < keys.Length && keys[i] == key && Languages2Values[i] != 0; i++)
            {
                result.Add(Languages2Values[i]);
            }

            return [.. result];
        }

        if (firstLength == 3)
        {
            var index = Languages3Keys.BinarySearch(key);
            if (index >= 0)
                return [Languages3Values[index]];

            var multi = Languages3MultiKeys.BinarySearch(key);
            if (multi >= 0)
                return Languages3MultiValues.Slice((int)Languages3MultiOffsets[multi], (int)Languages3MultiCounts[multi]).ToArray();

            if (Languages3Blocked.BinarySearch(key) >= 0)
                return [];

            // An ISO 639-3 code: its tag in uppercase
            return [key & ~0x20202000u];
        }

        return [];
    }

    /// <summary>A tag from up to 4 characters, padded with spaces.</summary>
    private static uint ToTag(ReadOnlySpan<char> value)
    {
        var tag = 0u;
        for (var i = 0; i < 4; i++)
        {
            tag = (tag << 8) | (i < value.Length ? (byte)value[i] : (byte)' ');
        }

        return tag;
    }

    /// <summary>The tags of the rules of the language tags with several subtags (HarfBuzz's <c>hb_ot_tags_from_complex_language</c>), or null.</summary>
    private static uint[]? FromComplexLanguage(string tag, int limit)
    {
        var firstCharacters = ComplexRuleFirstCharacters;
        var conditionStarts = ComplexRuleConditionStarts;
        var conditionKinds = ComplexRuleConditionKinds;
        var conditionValues = ComplexRuleConditionValues;
        var tagStarts = ComplexRuleTagStarts;

        // The rules of the variant subtags apply to the tags with a subtag and at least 7 characters
        var dash = tag.IndexOf('-', StringComparison.Ordinal);
        var variantsApply = limit >= 7 && dash >= 0 && dash < limit && limit - dash >= 5;
        for (var rule = 0; rule < firstCharacters.Length; rule++)
        {
            var first = firstCharacters[rule];
            if (first == '\0' ? !variantsApply : tag[0] != first)
                continue;

            var matches = true;
            for (var condition = (int)conditionStarts[rule]; condition < conditionStarts[rule + 1] && matches; condition++)
            {
                var value = conditionValues[condition];
                matches = conditionKinds[condition] switch
                {
                    // The tag starts with the value, followed by its end or a subtag
                    0 => limit >= value.Length && tag.StartsWith(value, StringComparison.Ordinal) && (tag.Length == value.Length || tag[value.Length] == '-'),
                    1 => tag == value,
                    2 => tag.StartsWith(value, StringComparison.Ordinal),
                    _ => HasSubtag(tag, limit, value),
                };
            }

            if (matches)
                return ComplexRuleTags[(int)tagStarts[rule]..(int)tagStarts[rule + 1]].ToArray();
        }

        return null;
    }

    /// <summary>Whether the tag contains a subtag (the value starts with a hyphen) before <paramref name="limit"/> (HarfBuzz's <c>subtag_matches</c>).</summary>
    private static bool HasSubtag(string tag, int limit, string subtag)
    {
        if (limit < subtag.Length)
            return false;

        var start = 0;
        while (true)
        {
            var index = tag.IndexOf(subtag, start, StringComparison.Ordinal);
            if (index < 0 || index >= limit)
                return false;

            var end = index + subtag.Length;
            if (end >= tag.Length || !char.IsAsciiLetterOrDigit(tag[end]))
                return true;

            start = end;
        }
    }
}
