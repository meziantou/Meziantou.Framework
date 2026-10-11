namespace Meziantou.Framework.Imaging.Internals;

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
    public const uint Cff = ((uint)'C' << 24) | ((uint)'F' << 16) | ((uint)'F' << 8) | ' ';
    public const uint Cff2 = ((uint)'C' << 24) | ((uint)'F' << 16) | ((uint)'F' << 8) | '2';

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
