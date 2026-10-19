namespace Meziantou.Framework.Fonts;

/// <summary>The four-byte tags of the font containers and tables, as big-endian integers.</summary>
internal static class FontTags
{
    // Signatures of the containers
    public const uint TrueType = 0x00010000;
    public const uint AppleTrueType = ((uint)'t' << 24) | ((uint)'r' << 16) | ((uint)'u' << 8) | 'e';
    public const uint OpenTypeCff = ((uint)'O' << 24) | ((uint)'T' << 16) | ((uint)'T' << 8) | 'O';
    public const uint PostScriptType1 = ((uint)'t' << 24) | ((uint)'y' << 16) | ((uint)'p' << 8) | '1';
    public const uint Collection = ((uint)'t' << 24) | ((uint)'t' << 16) | ((uint)'c' << 8) | 'f';
    public const uint Woff = ((uint)'w' << 24) | ((uint)'O' << 16) | ((uint)'F' << 8) | 'F';
    public const uint Woff2 = ((uint)'w' << 24) | ((uint)'O' << 16) | ((uint)'F' << 8) | '2';

    // Tables
    public const uint Cmap = ((uint)'c' << 24) | ((uint)'m' << 16) | ((uint)'a' << 8) | 'p';
    public const uint Glyf = ((uint)'g' << 24) | ((uint)'l' << 16) | ((uint)'y' << 8) | 'f';
    public const uint Head = ((uint)'h' << 24) | ((uint)'e' << 16) | ((uint)'a' << 8) | 'd';
    public const uint Hhea = ((uint)'h' << 24) | ((uint)'h' << 16) | ((uint)'e' << 8) | 'a';
    public const uint Hmtx = ((uint)'h' << 24) | ((uint)'m' << 16) | ((uint)'t' << 8) | 'x';
    public const uint Loca = ((uint)'l' << 24) | ((uint)'o' << 16) | ((uint)'c' << 8) | 'a';
    public const uint Maxp = ((uint)'m' << 24) | ((uint)'a' << 16) | ((uint)'x' << 8) | 'p';
    public const uint Name = ((uint)'n' << 24) | ((uint)'a' << 16) | ((uint)'m' << 8) | 'e';
    public const uint Os2 = ((uint)'O' << 24) | ((uint)'S' << 16) | ((uint)'/' << 8) | '2';
    public const uint Post = ((uint)'p' << 24) | ((uint)'o' << 16) | ((uint)'s' << 8) | 't';
    public const uint Kern = ((uint)'k' << 24) | ((uint)'e' << 16) | ((uint)'r' << 8) | 'n';
    public const uint Gdef = ((uint)'G' << 24) | ((uint)'D' << 16) | ((uint)'E' << 8) | 'F';
    public const uint Gsub = ((uint)'G' << 24) | ((uint)'S' << 16) | ((uint)'U' << 8) | 'B';
    public const uint Gpos = ((uint)'G' << 24) | ((uint)'P' << 16) | ((uint)'O' << 8) | 'S';
    public const uint Cff = ((uint)'C' << 24) | ((uint)'F' << 16) | ((uint)'F' << 8) | ' ';
    public const uint Cff2 = ((uint)'C' << 24) | ((uint)'F' << 16) | ((uint)'F' << 8) | '2';
    public const uint Fvar = ((uint)'f' << 24) | ((uint)'v' << 16) | ((uint)'a' << 8) | 'r';
    public const uint Avar = ((uint)'a' << 24) | ((uint)'v' << 16) | ((uint)'a' << 8) | 'r';
    public const uint Gvar = ((uint)'g' << 24) | ((uint)'v' << 16) | ((uint)'a' << 8) | 'r';
    public const uint Hvar = ((uint)'H' << 24) | ((uint)'V' << 16) | ((uint)'A' << 8) | 'R';
    public const uint Mvar = ((uint)'M' << 24) | ((uint)'V' << 16) | ((uint)'A' << 8) | 'R';
    public const uint Vhea = ((uint)'v' << 24) | ((uint)'h' << 16) | ((uint)'e' << 8) | 'a';
    public const uint Vmtx = ((uint)'v' << 24) | ((uint)'m' << 16) | ((uint)'t' << 8) | 'x';
    public const uint Vorg = ((uint)'V' << 24) | ((uint)'O' << 16) | ((uint)'R' << 8) | 'G';
    public const uint Vvar = ((uint)'V' << 24) | ((uint)'V' << 16) | ((uint)'A' << 8) | 'R';
    public const uint Base = ((uint)'B' << 24) | ((uint)'A' << 16) | ((uint)'S' << 8) | 'E';
    public const uint Colr = ((uint)'C' << 24) | ((uint)'O' << 16) | ((uint)'L' << 8) | 'R';
    public const uint Cpal = ((uint)'C' << 24) | ((uint)'P' << 16) | ((uint)'A' << 8) | 'L';
    public const uint Sbix = ((uint)'s' << 24) | ((uint)'b' << 16) | ((uint)'i' << 8) | 'x';
    public const uint Cblc = ((uint)'C' << 24) | ((uint)'B' << 16) | ((uint)'L' << 8) | 'C';
    public const uint Cbdt = ((uint)'C' << 24) | ((uint)'B' << 16) | ((uint)'D' << 8) | 'T';
    public const uint Eblc = ((uint)'E' << 24) | ((uint)'B' << 16) | ((uint)'L' << 8) | 'C';
    public const uint Ebdt = ((uint)'E' << 24) | ((uint)'B' << 16) | ((uint)'D' << 8) | 'T';
    public const uint Math = ((uint)'M' << 24) | ((uint)'A' << 16) | ((uint)'T' << 8) | 'H';

    // Axes of a variable font
    public const uint WeightAxis = ((uint)'w' << 24) | ((uint)'g' << 16) | ((uint)'h' << 8) | 't';

    // Metrics the MVAR table varies: OpenType specification, "MVAR", "Value tags"
    public const uint MetricAscender = ((uint)'h' << 24) | ((uint)'a' << 16) | ((uint)'s' << 8) | 'c';
    public const uint MetricDescender = ((uint)'h' << 24) | ((uint)'d' << 16) | ((uint)'s' << 8) | 'c';
    public const uint MetricLineGap = ((uint)'h' << 24) | ((uint)'l' << 16) | ((uint)'g' << 8) | 'p';
    public const uint MetricWinAscent = ((uint)'h' << 24) | ((uint)'c' << 16) | ((uint)'l' << 8) | 'a';
    public const uint MetricWinDescent = ((uint)'h' << 24) | ((uint)'c' << 16) | ((uint)'l' << 8) | 'd';
    public const uint MetricXHeight = ((uint)'x' << 24) | ((uint)'h' << 16) | ((uint)'g' << 8) | 't';
    public const uint MetricCapHeight = ((uint)'c' << 24) | ((uint)'p' << 16) | ((uint)'h' << 8) | 't';
    public const uint MetricStrikeoutSize = ((uint)'s' << 24) | ((uint)'t' << 16) | ((uint)'r' << 8) | 's';
    public const uint MetricStrikeoutOffset = ((uint)'s' << 24) | ((uint)'t' << 16) | ((uint)'r' << 8) | 'o';
    public const uint MetricUnderlineSize = ((uint)'u' << 24) | ((uint)'n' << 16) | ((uint)'d' << 8) | 's';
    public const uint MetricUnderlineOffset = ((uint)'u' << 24) | ((uint)'n' << 16) | ((uint)'d' << 8) | 'o';
    public const uint MetricSubscriptYOffset = ((uint)'s' << 24) | ((uint)'b' << 16) | ((uint)'y' << 8) | 'o';
    public const uint MetricSuperscriptYOffset = ((uint)'s' << 24) | ((uint)'p' << 16) | ((uint)'y' << 8) | 'o';
    public const uint MetricVerticalAscender = ((uint)'v' << 24) | ((uint)'a' << 16) | ((uint)'s' << 8) | 'c';
    public const uint MetricVerticalDescender = ((uint)'v' << 24) | ((uint)'d' << 16) | ((uint)'s' << 8) | 'c';
    public const uint MetricVerticalLineGap = ((uint)'v' << 24) | ((uint)'l' << 16) | ((uint)'g' << 8) | 'p';

    /// <summary>Formats a tag for a message: its four characters when they are printable, its hexadecimal value otherwise.</summary>
    public static string Format(uint tag)
    {
        Span<char> text = stackalloc char[4];
        for (var i = 0; i < 4; i++)
        {
            var value = (char)((tag >> (24 - (i * 8))) & 0xFF);
            if (value is < ' ' or > '~')
                return "0x" + tag.ToString("X8", CultureInfo.InvariantCulture);

            text[i] = value;
        }

        return new string(text);
    }
}
