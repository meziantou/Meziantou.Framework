namespace Meziantou.Framework.Fonts;

/// <summary>
/// Finds the language systems of the OpenType Layout tables a text in a language can use: the tags a font can have a
/// language system under, for a BCP 47 language tag.
/// </summary>
/// <remarks>
/// <para>
/// A language system tag of OpenType is not a language code: the registry of the specification ("Language system
/// tags") gives, for each tag, the ISO 639 codes of the languages it is for. One code can have several tags (a language
/// and a group of languages it belongs to, or two written forms of a language), so the result is a list by order of
/// preference: the first tag a font has for the script of the text is the language system that is used, and the
/// default language system of the script is used when the font has none of them.
/// </para>
/// <para>
/// The table (<c>OpenTypeLanguageTable.g.cs</c>) is transcribed from the registry: for a code, the tag of fewer
/// languages comes first. It is completed with what the language subtag registry of BCP 47 says: a language that
/// belongs to a macrolanguage also has the tags of the macrolanguage, and a deprecated subtag has the tags of the
/// subtag that replaces it. The rules of this file are the ones the codes alone cannot give, each from what the
/// registry says of the tag:
/// </para>
/// <list type="bullet">
/// <item><description>
/// the tags of the phonetic transcriptions, which the registry relates to the variant subtags <c>fonipa</c>,
/// <c>fonnapa</c> and <c>fonupa</c>;
/// </description></item>
/// <item><description>
/// the tags of a written form of a language, which are used when the language tag says so and not otherwise: the
/// three forms of the Syriac script (script subtags <c>Syre</c>, <c>Syrj</c>, <c>Syrn</c>), polytonic Greek (variant
/// <c>polyton</c>), Khutsuri Georgian (script subtag <c>Geok</c>);
/// </description></item>
/// <item><description>
/// Chinese and the languages it encompasses, whose five tags all have the code of Chinese: the script and the region
/// choose (simplified by default, traditional for <c>Hant</c> and Taiwan, the tags of Hong Kong and Macao for these
/// regions, the one of Hong Kong for Cantonese and traditional for Literary Chinese when the tag says nothing else,
/// phonetic for a text in Bopomofo or Latin letters);
/// </description></item>
/// <item><description>
/// the Romanian of Moldova (<c>ro-MD</c>) and the Arabic of Morocco (<c>ary</c>), which have a tag the registry
/// gives no code for, or the code of a subtag that is not used anymore;
/// </description></item>
/// <item><description>
/// two subtags of two letters that name a macrolanguage the registry has no tag for: <c>no</c> is Norwegian Bokmål
/// and <c>qu</c> is the Quechua of Cusco.
/// </description></item>
/// </list>
/// <para>
/// A macrolanguage does not get the tags of the languages it encompasses, and the tags BCP 47 keeps from older
/// standards (<c>i-klingon</c>, <c>art-lojban</c>) are not known: both give no tag.
/// </para>
/// </remarks>
internal static partial class OpenTypeLanguage
{
    private const int RecordSize = 5;

    /// <summary>The most tags a language has: the tags of a rule, then the ones of the table.</summary>
    private const int MaxTags = 8;

    private static readonly uint Syre = Tag("SYRE");
    private static readonly uint Syrj = Tag("SYRJ");
    private static readonly uint Syrn = Tag("SYRN");
    private static readonly uint PolytonicGreek = Tag("PGR ");
    private static readonly uint KhutsuriGeorgian = Tag("KGE ");
    private static readonly uint SimplifiedChinese = Tag("ZHS ");

    /// <summary>
    /// Determines whether a text has the form of a BCP 47 language tag: subtags of one to eight letters and digits
    /// separated by hyphens, the first one made of letters.
    /// </summary>
    /// <remarks>The subtags are not checked against the registry of subtags: a tag that names nothing gives no language system.</remarks>
    public static bool IsWellFormed(ReadOnlySpan<char> languageTag)
    {
        var isFirst = true;
        foreach (var range in languageTag.Split('-'))
        {
            var subtag = languageTag[range];
            if (subtag.Length is 0 or > 8)
                return false;

            foreach (var character in subtag)
            {
                if (!(char.IsAsciiLetter(character) || (!isFirst && char.IsAsciiDigit(character))))
                    return false;
            }

            isFirst = false;
        }

        return !isFirst;
    }

    /// <summary>Gets the tags of the language systems of a language, by order of preference.</summary>
    /// <param name="languageTag">A well-formed BCP 47 language tag, in any case.</param>
    /// <returns>The tags; empty when the registry of OpenType has no tag for the language.</returns>
    public static uint[] GetTags(ReadOnlySpan<char> languageTag)
    {
        Span<uint> tags = stackalloc uint[MaxTags];
        var count = 0;

        // The subtags: language, extended language, script, region, variants. What follows a subtag of one character
        // (an extension, private use) says nothing of the language systems.
        Span<char> language = stackalloc char[3];
        Span<char> extended = stackalloc char[3];
        Span<char> script = stackalloc char[4];
        Span<char> region = stackalloc char[3];
        var languageLength = 0;
        var extendedLength = 0;
        var scriptLength = 0;
        var regionLength = 0;
        var isPolytonic = false;
        var index = 0;
        foreach (var range in languageTag.Split('-'))
        {
            var subtag = languageTag[range];
            if (subtag.Length == 1)
                break;

            if (index == 0)
            {
                // A language of four letters or more is not an ISO 639 code.
                if (subtag.Length > 3)
                    return [];

                languageLength = subtag.ToLowerInvariant(language);
            }
            else if (subtag.Length == 3 && index == 1 && char.IsAsciiLetter(subtag[0]))
            {
                extendedLength = subtag.ToLowerInvariant(extended);
            }
            else if (subtag.Length == 4 && char.IsAsciiLetter(subtag[0]) && scriptLength == 0 && regionLength == 0)
            {
                scriptLength = subtag.ToLowerInvariant(script);
            }
            else if (regionLength == 0 && (subtag.Length == 2 || (subtag.Length == 3 && char.IsAsciiDigit(subtag[0]))))
            {
                regionLength = subtag.ToUpperInvariant(region);
            }
            else if (subtag.Equals("fonipa", StringComparison.OrdinalIgnoreCase))
            {
                Add(tags, ref count, Tag("IPPH"));
            }
            else if (subtag.Equals("fonnapa", StringComparison.OrdinalIgnoreCase))
            {
                Add(tags, ref count, Tag("APPH"));
            }
            else if (subtag.Equals("fonupa", StringComparison.OrdinalIgnoreCase))
            {
                Add(tags, ref count, Tag("UPPH"));
            }
            else if (subtag.Equals("polyton", StringComparison.OrdinalIgnoreCase))
            {
                isPolytonic = true;
            }

            index++;
        }

        // An extended language is the language of the text ("zh-yue" is "yue"); the language before it is its group.
        ReadOnlySpan<char> code = extendedLength != 0 ? extended[..extendedLength] : language[..languageLength];
        ReadOnlySpan<char> scriptCode = script[..scriptLength];
        ReadOnlySpan<char> regionCode = region[..regionLength];
        code = code switch
        {
            "no" => "nb",
            "qu" => "quz",
            _ => code,
        };

        // The written form the language tag asks for.
        var form = scriptCode switch
        {
            "syre" => Syre,
            "syrj" => Syrj,
            "syrn" => Syrn,
            "geok" => KhutsuriGeorgian,
            _ => isPolytonic ? PolytonicGreek : 0u,
        };

        // The Romanian of Moldova has a tag of its own, under a code that is not used anymore ("mo" is now "ro-MD").
        if (code is "mo" || (code is "ro" or "ron" && regionCode is "MD"))
        {
            Add(tags, ref count, Tag("MOL "));
            code = "ro";
        }

        // The registry has a tag for the Arabic of Morocco, and gives no code for it.
        if (code is "ary")
        {
            Add(tags, ref count, Tag("MOR "));
        }

        var list = FindTags(code);
        AddTags(tags, ref count, list, form);
        if (HasTag(list, SimplifiedChinese))
        {
            AddChinese(tags, ref count, code, scriptCode, regionCode);
        }

        return tags[..count].ToArray();
    }

    /// <summary>
    /// Adds the tags of Chinese and of the languages it encompasses: its tags all have the code of the language, and the
    /// script and the region choose.
    /// </summary>
    private static void AddChinese(Span<uint> tags, ref int count, ReadOnlySpan<char> code, ReadOnlySpan<char> script, ReadOnlySpan<char> region)
    {
        // A text in Bopomofo or in Latin letters is a phonetic transcription.
        if (script is "bopo" or "latn")
        {
            Add(tags, ref count, Tag("ZHP "));
            return;
        }

        // The region gives the script when the tag does not: traditional characters in Taiwan, Hong Kong and Macao;
        // without a region, for Cantonese, which is written in Hong Kong more than anywhere else, and for Literary Chinese.
        var isCantonese = code is "yue" && region.IsEmpty;
        var isTraditional = script is "hant" || (script.IsEmpty && (isCantonese || region is "TW" or "HK" or "MO" || (code is "lzh" && region.IsEmpty)));
        if (!isTraditional)
        {
            Add(tags, ref count, SimplifiedChinese);
            return;
        }

        if (region is "MO")
        {
            Add(tags, ref count, Tag("ZHTM"));
        }

        // The forms of Hong Kong are the closest ones to those of Macao, and the ones of Cantonese.
        if (region is "HK" or "MO" || isCantonese)
        {
            Add(tags, ref count, Tag("ZHH "));
        }

        Add(tags, ref count, Tag("ZHT "));
    }

    /// <summary>
    /// Adds the tags of a list, without the tags of a written form the language tag does not ask for and without the
    /// tags of Chinese, which the script and the region choose.
    /// </summary>
    private static void AddTags(Span<uint> tags, ref int count, ReadOnlySpan<byte> list, uint form)
    {
        // The form that is asked for comes first, when it is one of the forms of the language.
        if (form != 0 && HasTag(list, form))
        {
            Add(tags, ref count, form);
        }

        for (var i = 0; i < list.Length; i += 4)
        {
            var tag = ReadTag(list[i..]);
            if (tag != Syre && tag != Syrj && tag != Syrn && tag != PolytonicGreek && tag != KhutsuriGeorgian && !IsChinese(tag))
            {
                Add(tags, ref count, tag);
            }
        }
    }

    private static bool IsChinese(uint tag) => tag == SimplifiedChinese || tag == Tag("ZHT ") || tag == Tag("ZHH ") || tag == Tag("ZHTM") || tag == Tag("ZHP ");

    private static bool HasTag(ReadOnlySpan<byte> list, uint tag)
    {
        for (var i = 0; i < list.Length; i += 4)
        {
            if (ReadTag(list[i..]) == tag)
                return true;
        }

        return false;
    }

    /// <summary>Finds the tags of a language subtag of two or three lowercase letters in the table.</summary>
    private static ReadOnlySpan<byte> FindTags(ReadOnlySpan<char> code)
    {
        if (code.Length is not (2 or 3))
            return [];

        var key = (code[0] << 16) | (code[1] << 8) | (code.Length == 3 ? code[2] : ' ');
        var codes = Codes;
        var low = 0;
        var high = CodeCount - 1;
        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            var record = codes.Slice(middle * RecordSize, RecordSize);
            var value = (record[0] << 16) | (record[1] << 8) | record[2];
            if (value == key)
            {
                var list = TagLists[((record[3] << 8) | record[4])..];
                return list.Slice(1, list[0] * 4);
            }

            if (value < key)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return [];
    }

    private static void Add(Span<uint> tags, ref int count, uint tag)
    {
        if (count < tags.Length && !tags[..count].Contains(tag))
        {
            tags[count++] = tag;
        }
    }

    private static uint ReadTag(ReadOnlySpan<byte> data) => ((uint)data[0] << 24) | ((uint)data[1] << 16) | ((uint)data[2] << 8) | data[3];

    private static uint Tag(string tag) => ShapingFeature.ToTag(tag);
}
