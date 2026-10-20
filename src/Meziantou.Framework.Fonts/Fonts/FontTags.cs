// Compiled into Meziantou.Framework.Fonts and Meziantou.Framework.Imaging, internal to each. The copy of Imaging is in
// its namespace: its tests see the internals of both assemblies, and find that copy first.
#if MEZIANTOU_FRAMEWORK_IMAGING
namespace Meziantou.Framework.Imaging;
#else
namespace Meziantou.Framework.Fonts;
#endif

/// <summary>The four-byte tags of the font containers and tables, as big-endian integers.</summary>
internal static class FontTags
{
    // Signatures of the containers
    /// <summary>The signature of a TrueType font (0x00010000).</summary>
    public const uint TrueType = 0x00010000;

    /// <summary>The signature 'true' of a TrueType font made for Apple platforms.</summary>
    public const uint AppleTrueType = ((uint)'t' << 24) | ((uint)'r' << 16) | ((uint)'u' << 8) | 'e';

    /// <summary>The signature 'OTTO' of an OpenType font with CFF outlines.</summary>
    public const uint OpenTypeCff = ((uint)'O' << 24) | ((uint)'T' << 16) | ((uint)'T' << 8) | 'O';

    /// <summary>The signature 'typ1' of a PostScript Type 1 font in an sfnt wrapper.</summary>
    public const uint PostScriptType1 = ((uint)'t' << 24) | ((uint)'y' << 16) | ((uint)'p' << 8) | '1';

    /// <summary>The signature 'ttcf' of a font collection.</summary>
    public const uint Collection = ((uint)'t' << 24) | ((uint)'t' << 16) | ((uint)'c' << 8) | 'f';

    /// <summary>The signature 'wOFF' of a WOFF 1.0 file.</summary>
    public const uint Woff = ((uint)'w' << 24) | ((uint)'O' << 16) | ((uint)'F' << 8) | 'F';

    /// <summary>The signature 'wOF2' of a WOFF 2.0 file.</summary>
    public const uint Woff2 = ((uint)'w' << 24) | ((uint)'O' << 16) | ((uint)'F' << 8) | '2';

    // Tables
    /// <summary>The tag of the 'cmap' table.</summary>
    public const uint Cmap = ((uint)'c' << 24) | ((uint)'m' << 16) | ((uint)'a' << 8) | 'p';

    /// <summary>The tag of the 'glyf' table.</summary>
    public const uint Glyf = ((uint)'g' << 24) | ((uint)'l' << 16) | ((uint)'y' << 8) | 'f';

    /// <summary>The tag of the 'head' table.</summary>
    public const uint Head = ((uint)'h' << 24) | ((uint)'e' << 16) | ((uint)'a' << 8) | 'd';

    /// <summary>The tag of the 'hhea' table.</summary>
    public const uint Hhea = ((uint)'h' << 24) | ((uint)'h' << 16) | ((uint)'e' << 8) | 'a';

    /// <summary>The tag of the 'hmtx' table.</summary>
    public const uint Hmtx = ((uint)'h' << 24) | ((uint)'m' << 16) | ((uint)'t' << 8) | 'x';

    /// <summary>The tag of the 'loca' table.</summary>
    public const uint Loca = ((uint)'l' << 24) | ((uint)'o' << 16) | ((uint)'c' << 8) | 'a';

    /// <summary>The tag of the 'maxp' table.</summary>
    public const uint Maxp = ((uint)'m' << 24) | ((uint)'a' << 16) | ((uint)'x' << 8) | 'p';

    /// <summary>The tag of the 'name' table.</summary>
    public const uint Name = ((uint)'n' << 24) | ((uint)'a' << 16) | ((uint)'m' << 8) | 'e';

    /// <summary>The tag of the 'OS/2' table.</summary>
    public const uint Os2 = ((uint)'O' << 24) | ((uint)'S' << 16) | ((uint)'/' << 8) | '2';

    /// <summary>The tag of the 'post' table.</summary>
    public const uint Post = ((uint)'p' << 24) | ((uint)'o' << 16) | ((uint)'s' << 8) | 't';

    /// <summary>The tag of the 'kern' table.</summary>
    public const uint Kern = ((uint)'k' << 24) | ((uint)'e' << 16) | ((uint)'r' << 8) | 'n';

    /// <summary>The tag of the 'GDEF' table.</summary>
    public const uint Gdef = ((uint)'G' << 24) | ((uint)'D' << 16) | ((uint)'E' << 8) | 'F';

    /// <summary>The tag of the 'GSUB' table.</summary>
    public const uint Gsub = ((uint)'G' << 24) | ((uint)'S' << 16) | ((uint)'U' << 8) | 'B';

    /// <summary>The tag of the 'GPOS' table.</summary>
    public const uint Gpos = ((uint)'G' << 24) | ((uint)'P' << 16) | ((uint)'O' << 8) | 'S';

    /// <summary>The tag of the 'CFF ' table.</summary>
    public const uint Cff = ((uint)'C' << 24) | ((uint)'F' << 16) | ((uint)'F' << 8) | ' ';

    /// <summary>The tag of the 'CFF2' table.</summary>
    public const uint Cff2 = ((uint)'C' << 24) | ((uint)'F' << 16) | ((uint)'F' << 8) | '2';

    /// <summary>The tag of the 'fvar' table.</summary>
    public const uint Fvar = ((uint)'f' << 24) | ((uint)'v' << 16) | ((uint)'a' << 8) | 'r';

    /// <summary>The tag of the 'avar' table.</summary>
    public const uint Avar = ((uint)'a' << 24) | ((uint)'v' << 16) | ((uint)'a' << 8) | 'r';

    /// <summary>The tag of the 'gvar' table.</summary>
    public const uint Gvar = ((uint)'g' << 24) | ((uint)'v' << 16) | ((uint)'a' << 8) | 'r';

    /// <summary>The tag of the 'HVAR' table.</summary>
    public const uint Hvar = ((uint)'H' << 24) | ((uint)'V' << 16) | ((uint)'A' << 8) | 'R';

    /// <summary>The tag of the 'MVAR' table.</summary>
    public const uint Mvar = ((uint)'M' << 24) | ((uint)'V' << 16) | ((uint)'A' << 8) | 'R';

    /// <summary>The tag of the 'vhea' table.</summary>
    public const uint Vhea = ((uint)'v' << 24) | ((uint)'h' << 16) | ((uint)'e' << 8) | 'a';

    /// <summary>The tag of the 'vmtx' table.</summary>
    public const uint Vmtx = ((uint)'v' << 24) | ((uint)'m' << 16) | ((uint)'t' << 8) | 'x';

    /// <summary>The tag of the 'VORG' table.</summary>
    public const uint Vorg = ((uint)'V' << 24) | ((uint)'O' << 16) | ((uint)'R' << 8) | 'G';

    /// <summary>The tag of the 'VVAR' table.</summary>
    public const uint Vvar = ((uint)'V' << 24) | ((uint)'V' << 16) | ((uint)'A' << 8) | 'R';

    /// <summary>The tag of the 'BASE' table.</summary>
    public const uint Base = ((uint)'B' << 24) | ((uint)'A' << 16) | ((uint)'S' << 8) | 'E';

    /// <summary>The tag of the 'COLR' table.</summary>
    public const uint Colr = ((uint)'C' << 24) | ((uint)'O' << 16) | ((uint)'L' << 8) | 'R';

    /// <summary>The tag of the 'CPAL' table.</summary>
    public const uint Cpal = ((uint)'C' << 24) | ((uint)'P' << 16) | ((uint)'A' << 8) | 'L';

    /// <summary>The tag of the 'sbix' table.</summary>
    public const uint Sbix = ((uint)'s' << 24) | ((uint)'b' << 16) | ((uint)'i' << 8) | 'x';

    /// <summary>The tag of the 'CBLC' table.</summary>
    public const uint Cblc = ((uint)'C' << 24) | ((uint)'B' << 16) | ((uint)'L' << 8) | 'C';

    /// <summary>The tag of the 'CBDT' table.</summary>
    public const uint Cbdt = ((uint)'C' << 24) | ((uint)'B' << 16) | ((uint)'D' << 8) | 'T';

    /// <summary>The tag of the 'SVG ' table.</summary>
    public const uint Svg = ((uint)'S' << 24) | ((uint)'V' << 16) | ((uint)'G' << 8) | ' ';

    /// <summary>The tag of the 'EBLC' table.</summary>
    public const uint Eblc = ((uint)'E' << 24) | ((uint)'B' << 16) | ((uint)'L' << 8) | 'C';

    /// <summary>The tag of the 'EBDT' table.</summary>
    public const uint Ebdt = ((uint)'E' << 24) | ((uint)'B' << 16) | ((uint)'D' << 8) | 'T';

    /// <summary>The tag of the 'MATH' table.</summary>
    public const uint Math = ((uint)'M' << 24) | ((uint)'A' << 16) | ((uint)'T' << 8) | 'H';

    // Axes of a variable font
    /// <summary>The tag 'wght' of the weight axis of a variable font.</summary>
    public const uint WeightAxis = ((uint)'w' << 24) | ((uint)'g' << 16) | ((uint)'h' << 8) | 't';

    // Metrics the MVAR table varies: OpenType specification, "MVAR", "Value tags"
    /// <summary>The value tag 'hasc' of the horizontal ascender in the MVAR table.</summary>
    public const uint MetricAscender = ((uint)'h' << 24) | ((uint)'a' << 16) | ((uint)'s' << 8) | 'c';

    /// <summary>The value tag 'hdsc' of the horizontal descender in the MVAR table.</summary>
    public const uint MetricDescender = ((uint)'h' << 24) | ((uint)'d' << 16) | ((uint)'s' << 8) | 'c';

    /// <summary>The value tag 'hlgp' of the horizontal line gap in the MVAR table.</summary>
    public const uint MetricLineGap = ((uint)'h' << 24) | ((uint)'l' << 16) | ((uint)'g' << 8) | 'p';

    /// <summary>The value tag 'hcla' of the Windows ascent in the MVAR table.</summary>
    public const uint MetricWinAscent = ((uint)'h' << 24) | ((uint)'c' << 16) | ((uint)'l' << 8) | 'a';

    /// <summary>The value tag 'hcld' of the Windows descent in the MVAR table.</summary>
    public const uint MetricWinDescent = ((uint)'h' << 24) | ((uint)'c' << 16) | ((uint)'l' << 8) | 'd';

    /// <summary>The value tag 'xhgt' of the x-height in the MVAR table.</summary>
    public const uint MetricXHeight = ((uint)'x' << 24) | ((uint)'h' << 16) | ((uint)'g' << 8) | 't';

    /// <summary>The value tag 'cpht' of the cap height in the MVAR table.</summary>
    public const uint MetricCapHeight = ((uint)'c' << 24) | ((uint)'p' << 16) | ((uint)'h' << 8) | 't';

    /// <summary>The value tag 'strs' of the thickness of the strikeout in the MVAR table.</summary>
    public const uint MetricStrikeoutSize = ((uint)'s' << 24) | ((uint)'t' << 16) | ((uint)'r' << 8) | 's';

    /// <summary>The value tag 'stro' of the position of the strikeout in the MVAR table.</summary>
    public const uint MetricStrikeoutOffset = ((uint)'s' << 24) | ((uint)'t' << 16) | ((uint)'r' << 8) | 'o';

    /// <summary>The value tag 'unds' of the thickness of the underline in the MVAR table.</summary>
    public const uint MetricUnderlineSize = ((uint)'u' << 24) | ((uint)'n' << 16) | ((uint)'d' << 8) | 's';

    /// <summary>The value tag 'undo' of the position of the underline in the MVAR table.</summary>
    public const uint MetricUnderlineOffset = ((uint)'u' << 24) | ((uint)'n' << 16) | ((uint)'d' << 8) | 'o';

    /// <summary>The value tag 'sbyo' of the vertical offset of subscripts in the MVAR table.</summary>
    public const uint MetricSubscriptYOffset = ((uint)'s' << 24) | ((uint)'b' << 16) | ((uint)'y' << 8) | 'o';

    /// <summary>The value tag 'spyo' of the vertical offset of superscripts in the MVAR table.</summary>
    public const uint MetricSuperscriptYOffset = ((uint)'s' << 24) | ((uint)'p' << 16) | ((uint)'y' << 8) | 'o';

    /// <summary>The value tag 'vasc' of the vertical ascender in the MVAR table.</summary>
    public const uint MetricVerticalAscender = ((uint)'v' << 24) | ((uint)'a' << 16) | ((uint)'s' << 8) | 'c';

    /// <summary>The value tag 'vdsc' of the vertical descender in the MVAR table.</summary>
    public const uint MetricVerticalDescender = ((uint)'v' << 24) | ((uint)'d' << 16) | ((uint)'s' << 8) | 'c';

    /// <summary>The value tag 'vlgp' of the vertical line gap in the MVAR table.</summary>
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
