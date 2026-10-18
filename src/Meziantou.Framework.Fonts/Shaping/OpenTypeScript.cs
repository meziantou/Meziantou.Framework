namespace Meziantou.Framework.Fonts;

/// <summary>
/// Gives the script tags of the OpenType Layout tables for a Unicode script: OpenType specification, "Script Tags".
/// </summary>
internal static class OpenTypeScript
{
    public const uint Default = ((uint)'D' << 24) | ((uint)'F' << 16) | ((uint)'L' << 8) | 'T';
    private const uint LowercaseDefault = ((uint)'d' << 24) | ((uint)'f' << 16) | ((uint)'l' << 8) | 't';
    private const uint Latin = ((uint)'l' << 24) | ((uint)'a' << 16) | ((uint)'t' << 8) | 'n';

    /// <summary>The largest number of tags <see cref="GetTags"/> writes.</summary>
    public const int MaxTagCount = 6;

    /// <summary>
    /// Gets the tags to look for in a script list, by order of preference: the tags of the script, then the default
    /// script, then the tags fonts use in its place.
    /// </summary>
    public static int GetTags(UnicodeScript script, Span<uint> tags)
    {
        var count = 0;
        var code = UnicodeScripts.GetIso15924Code(script);
        switch (code)
        {
            // The scripts whose shaping model was revised have a tag per version of the model, the newest first.
            case "Beng":
                count = Add(tags, count, "bng3", "bng2", "beng");
                break;

            case "Deva":
                count = Add(tags, count, "dev3", "dev2", "deva");
                break;

            case "Gujr":
                count = Add(tags, count, "gjr3", "gjr2", "gujr");
                break;

            case "Guru":
                count = Add(tags, count, "gur3", "gur2", "guru");
                break;

            case "Knda":
                count = Add(tags, count, "knd3", "knd2", "knda");
                break;

            case "Mlym":
                count = Add(tags, count, "mlm3", "mlm2", "mlym");
                break;

            case "Orya":
                count = Add(tags, count, "ory3", "ory2", "orya");
                break;

            case "Taml":
                count = Add(tags, count, "tml3", "tml2", "taml");
                break;

            case "Telu":
                count = Add(tags, count, "tel3", "tel2", "telu");
                break;

            case "Mymr":
                count = Add(tags, count, "mym2", "mymr");
                break;

            // The tags that are not the ISO 15924 code in lowercase.
            case "Hira" or "Kana":
                count = Add(tags, count, "kana");
                break;

            case "Laoo":
                count = Add(tags, count, "lao ");
                break;

            case "Yiii":
                count = Add(tags, count, "yi  ");
                break;

            case "Nkoo":
                count = Add(tags, count, "nko ");
                break;

            case "Vaii":
                count = Add(tags, count, "vai ");
                break;

            case "Zmth":
                count = Add(tags, count, "math");
                break;

            // No script: the default one.
            case "Zyyy" or "Zinh" or "Zzzz":
                break;

            default:
                tags[count++] = ((uint)char.ToLowerInvariant(code[0]) << 24) | ((uint)code[1] << 16) | ((uint)code[2] << 8) | code[3];
                break;
        }

        tags[count++] = Default;
        tags[count++] = LowercaseDefault;
        tags[count++] = Latin;
        return count;
    }

    private static int Add(Span<uint> tags, int count, params ReadOnlySpan<string> values)
    {
        foreach (var value in values)
        {
            tags[count++] = ShapingFeature.ToTag(value);
        }

        return count;
    }
}
