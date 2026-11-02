namespace Meziantou.Framework.Pdf.Text;

/// <summary>
/// The predefined CMaps of ISO 32000-2 (9.7.5.2, Table 116) and the other CMaps of the Adobe CMap resources: their code
/// space ranges (from the <c>begincodespacerange</c> of each CMap of adobe-type-tools/cmap-resources), their writing
/// mode (the names that end with <c>V</c>) and the character encoding of their codes.
/// </summary>
internal static class PredefinedCMaps
{
    private static readonly Dictionary<string, (CMapCharset? Charset, (int Length, uint Low, uint High)[] CodeSpaces)> Definitions = CreateDefinitions();

    public static (bool IsVertical, CMapCharset? Charset, (int Length, uint Low, uint High)[] CodeSpaces)? TryGet(string name)
    {
        if (!Definitions.TryGetValue(name, out var definition))
            return null;

        return (name is "V" || name.EndsWith("-V", StringComparison.Ordinal), definition.Charset, definition.CodeSpaces);
    }

    private static Dictionary<string, (CMapCharset?, (int, uint, uint)[])> CreateDefinitions()
    {
        (int, uint, uint)[] shiftJis = [(1, 0x00, 0x80), (2, 0x8140, 0x9FFC), (1, 0xA0, 0xDF), (2, 0xE040, 0xFCFC)];
        (int, uint, uint)[] shiftJisMac = [.. shiftJis, (1, 0xFD, 0xFF)];
        (int, uint, uint)[] eucJapanese = [(1, 0x00, 0x80), (2, 0x8EA0, 0x8EDF), (2, 0xA1A1, 0xFEFE)];
        (int, uint, uint)[] rowCell = [(2, 0x2121, 0x7E7E)];
        (int, uint, uint)[] euc = [(1, 0x00, 0x80), (2, 0xA1A1, 0xFEFE)];
        (int, uint, uint)[] eucMac = [(1, 0x00, 0x80), (2, 0xA1A1, 0xFCFE), (1, 0xFD, 0xFF)];
        (int, uint, uint)[] gbk = [(1, 0x00, 0x80), (2, 0x8140, 0xFEFE)];
        (int, uint, uint)[] gb18030 = [(1, 0x00, 0x7F), (4, 0x81308130, 0xFE39FE39), (2, 0x8140, 0xFEFE)];
        (int, uint, uint)[] big5 = [(1, 0x00, 0x80), (2, 0xA140, 0xFEFE)];
        (int, uint, uint)[] big5Mac = [(1, 0x00, 0x80), (2, 0xA140, 0xFCFE), (1, 0xFD, 0xFF)];
        (int, uint, uint)[] cnsEuc = [(1, 0x00, 0x80), (4, 0x8EA1A1A1, 0x8EA1FEFE), (4, 0x8EA2A1A1, 0x8EA2FEFE), (4, 0x8EA3A1A1, 0x8EA3FEFE), (2, 0xA1A1, 0xFEFE)];
        (int, uint, uint)[] koreanMac = [(1, 0x00, 0x84), (2, 0xA141, 0xFDFE), (1, 0xFE, 0xFF)];
        (int, uint, uint)[] uhc = [(1, 0x00, 0x80), (2, 0x8141, 0xFEFE)];
        (int, uint, uint)[] johab = [(1, 0x00, 0x80), (2, 0x8441, 0xD3FE), (2, 0xD831, 0xDEFE), (2, 0xE031, 0xF9FE)];
        (int, uint, uint)[] hojoEuc = [(3, 0x8FA1A1, 0x8FFEFE)];
        (int, uint, uint)[] singleByte = [(1, 0x00, 0xFF)];
        (int, uint, uint)[] ucs2 = [(2, 0x0000, 0xD7FF), (2, 0xE000, 0xFFFF)];
        (int, uint, uint)[] utf16 = [(2, 0x0000, 0xD7FF), (4, 0xD800DC00, 0xDBFFDFFF), (2, 0xE000, 0xFFFF)];
        (int, uint, uint)[] utf32 = [(4, 0x00000000, 0x0010FFFF)];
        (int, uint, uint)[] utf8 = [(1, 0x00, 0x7F), (2, 0xC080, 0xDFBF), (3, 0xE08080, 0xEFBFBF), (4, 0xF0808080, 0xF7BFBFBF)];

        var shiftJisCharset = CMapCharset.CodePage(932);
        var eucJapaneseCharset = CMapCharset.CodePage(20932);
        var gbkCharset = CMapCharset.CodePage(936);
        var big5Charset = CMapCharset.CodePage(950);
        var koreanCharset = CMapCharset.CodePage(949);

        var result = new Dictionary<string, (CMapCharset?, (int, uint, uint)[])>(StringComparer.Ordinal);
        void Add(CMapCharset? charset, (int, uint, uint)[] codeSpaces, params string[] names)
        {
            foreach (var name in names)
            {
                result.Add(name, (charset, codeSpaces));

                // The vertical CMap uses the horizontal one: same code space ranges
                if (name == "H")
                {
                    result.Add("V", (charset, codeSpaces));
                }
                else if (name.EndsWith("-H", StringComparison.Ordinal))
                {
                    result.Add(name[..^1] + "V", (charset, codeSpaces));
                }
            }
        }

        // Japanese (Adobe-Japan1, Adobe-Japan2)
        Add(shiftJisCharset, shiftJis, "78-RKSJ-H", "78ms-RKSJ-H", "90ms-RKSJ-H", "90msp-RKSJ-H", "Add-RKSJ-H", "Ext-RKSJ-H", "RKSJ-H");
        Add(shiftJisCharset, shiftJisMac, "83pv-RKSJ-H", "90pv-RKSJ-H");
        Add(eucJapaneseCharset, eucJapanese, "78-EUC-H", "EUC-H");
        Add(CMapCharset.CodePage(20932, rowCell: true), rowCell, "78-H", "Add-H", "Ext-H", "H", "NWP-H");
        Add(eucJapaneseCharset, hojoEuc, "Hojo-EUC-H");
        Add(charset: null, rowCell, "Hojo-H");
        Add(charset: null, singleByte, "Hankaku", "Hiragana", "Katakana", "Roman", "WP-Symbol");

        // Simplified Chinese (Adobe-GB1); the GB/T 12345 CMaps encode traditional characters in GB 2312 codes
        Add(gbkCharset, euc, "GB-EUC-H");
        Add(gbkCharset, eucMac, "GBpc-EUC-H");
        Add(charset: null, euc, "GBT-EUC-H");
        Add(charset: null, eucMac, "GBTpc-EUC-H");
        Add(CMapCharset.CodePage(936, rowCell: true), rowCell, "GB-H");
        Add(charset: null, rowCell, "GBT-H");
        Add(gbkCharset, gbk, "GBK-EUC-H", "GBKp-EUC-H");
        Add(CMapCharset.CodePage(54936), gb18030, "GBK2K-H");

        // Traditional Chinese (Adobe-CNS1): the Hong Kong extensions of Big Five are not in code page 950
        Add(big5Charset, big5, "B5-H", "ETen-B5-H", "ETenms-B5-H", "HKdla-B5-H", "HKm314-B5-H", "HKm471-B5-H");
        Add(big5Charset, big5Mac, "B5pc-H");
        Add(big5Charset, [(1, 0x00, 0x80), (2, 0x8740, 0xFEFE)], "ETHK-B5-H", "HKscs-B5-H");
        Add(big5Charset, [(1, 0x00, 0x80), (2, 0x8E40, 0xFEFE)], "HKdlb-B5-H");
        Add(big5Charset, [(1, 0x00, 0x80), (2, 0x8A40, 0xFEFE)], "HKgccs-B5-H");
        Add(charset: null, cnsEuc, "CNS-EUC-H");
        Add(charset: null, rowCell, "CNS1-H", "CNS2-H");

        // Korean (Adobe-Korea1)
        Add(koreanCharset, euc, "KSC-EUC-H");
        Add(koreanCharset, koreanMac, "KSCpc-EUC-H");
        Add(koreanCharset, uhc, "KSCms-UHC-H", "KSCms-UHC-HW-H");
        Add(CMapCharset.CodePage(949, rowCell: true), rowCell, "KSC-H");
        Add(CMapCharset.CodePage(1361), johab, "KSC-Johab-H");

        // Unicode
        Add(CMapCharset.Utf16, ucs2, "UniCNS-UCS2-H", "UniGB-UCS2-H", "UniHojo-UCS2-H", "UniJIS-UCS2-H", "UniJIS-UCS2-HW-H", "UniKS-UCS2-H", "UniJISPro-UCS2-V", "UniJISPro-UCS2-HW-V");
        Add(CMapCharset.Utf16, utf16, "UniAKR-UTF16-H", "UniCNS-UTF16-H", "UniGB-UTF16-H", "UniHojo-UTF16-H", "UniJIS-UTF16-H", "UniJIS2004-UTF16-H", "UniKS-UTF16-H", "UniManga-UTF16-H");
        Add(CMapCharset.Utf32, utf32, "UniAKR-UTF32-H", "UniCNS-UTF32-H", "UniGB-UTF32-H", "UniHojo-UTF32-H", "UniJIS-UTF32-H", "UniJIS2004-UTF32-H", "UniJISX0213-UTF32-H", "UniJISX02132004-UTF32-H", "UniKS-UTF32-H", "UniManga-UTF32-H");
        Add(CMapCharset.Utf8, utf8, "UniAKR-UTF8-H", "UniCNS-UTF8-H", "UniGB-UTF8-H", "UniHojo-UTF8-H", "UniJIS-UTF8-H", "UniJIS2004-UTF8-H", "UniKS-UTF8-H", "UniManga-UTF8-H", "UniJISPro-UTF8-V");
        return result;
    }
}
