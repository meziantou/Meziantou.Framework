using System.Text;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Email.Fuzz;

/// <summary>
/// The de-encapsulating reader of [MS-OXRTFEX] reads any input without throwing, finds an encapsulated body exactly when the
/// header says so, and produces at most one character per byte of RTF. An HTML or a text encapsulated by the writer is
/// extracted unchanged.
/// </summary>
public static partial class EmailFuzzTargets
{
    private const string RtfSeedPrefix = "TestData/Rtf/";

    private static readonly string[] RtfTokens =
    [
        "{\\rtf1", "\\ansi", "\\ansicpg1252", "\\ansicpg932", "\\fromhtml1", "\\fromtext", "\\deff0", "{\\fonttbl", "{\\f1\\fcharset128 x;}", "\\fcharset204", "\\cpg1251", "\\f1",
        "{\\*\\htmltag", "{\\*\\htmltag64}", "{\\*\\mhtmltag", "\\htmlrtf", "\\htmlrtf0", "\\htmlbase", "{\\colortbl;}", "{\\*\\generator x;}", "{\\pict", "\\bin4 ",
        "\\par", "\\line", "\\tab", "\\lquote", "\\rdblquote", "\\bullet", "\\emdash", "\\~", "\\_", "\\'e9", "\\'82\\'a0", "\\u8364?", "\\u-3913?", "\\uc2", "\\uc0", "\\{", "\\}", "\\\\", "\\*",
    ];

    private static IReadOnlyList<FuzzTarget> RtfTargets { get; } =
    [
        new("rtf-decapsulate", RtfDecapsulate) { Seeds = GetRtfSeeds, Dictionary = RtfTokens },
        new("rtf-encapsulate", RtfEncapsulate) { Seeds = GetRtfTextSeeds },
    ];

    private static void RtfDecapsulate(ReadOnlySpan<byte> data)
    {
        var format = RtfEncapsulation.GetFormat(data);
        var result = RtfEncapsulation.Decapsulate(data);
        FuzzAssert.Equal(format == RtfEncapsulationFormat.None, result is null, "The RTF has an encapsulated body exactly when its header says so");
        if (result is not null)
        {
            FuzzAssert.True(result.Length <= data.Length, "The encapsulated body has more characters than the RTF has bytes");
        }
    }

    private static void RtfEncapsulate(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        var html = RtfEncapsulation.EncapsulateHtml(text);
        FuzzAssert.Equal(RtfEncapsulationFormat.Html, RtfEncapsulation.GetFormat(html), "The encapsulated HTML is not recognized");
        FuzzAssert.Equal(text, RtfEncapsulation.Decapsulate(html), "The encapsulated HTML is not extracted unchanged");

        var plain = RtfEncapsulation.EncapsulateText(text);
        FuzzAssert.Equal(RtfEncapsulationFormat.Text, RtfEncapsulation.GetFormat(plain), "The encapsulated text is not recognized");
        FuzzAssert.Equal(text, RtfEncapsulation.Decapsulate(plain), "The encapsulated text is not extracted unchanged");
    }

    private static IEnumerable<FuzzSeed> GetRtfSeeds()
    {
        var assembly = typeof(EmailFuzzTargets).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(RtfSeedPrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, name);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return new FuzzSeed(name[RtfSeedPrefix.Length..], memory.ToArray());
        }

        yield return new FuzzSeed("html", RtfEncapsulation.EncapsulateHtml("<p>Caf\u00E9 {x}</p>\r\n\t\\"));
        yield return new FuzzSeed("text", RtfEncapsulation.EncapsulateText("Line 1\r\nLine 2\n"));
        yield return new FuzzSeed("fonts", Encoding.ASCII.GetBytes("{\\rtf1\\ansi\\ansicpg1252\\fromhtml1 {\\fonttbl{\\f0\\fswiss Arial;}{\\f1\\fnil\\fcharset128 MS Gothic;}}\\f1\\'82\\'a0\\htmlrtf {\\b x}\\htmlrtf0 \\uc2\\u8364\\'80\\'80{\\*\\htmltag <br>\\par}}"));
        yield return new FuzzSeed("rtf", Encoding.ASCII.GetBytes("{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0 Arial;}}\\pard Hello\\par}"));
    }

    private static IEnumerable<FuzzSeed> GetRtfTextSeeds()
    {
        yield return new FuzzSeed("html", FuzzText.Encode("<html><body><p>Caf\u00E9 {x} \\ \t</p>\r\n\n\r</body></html>"));
        yield return new FuzzSeed("empty", Array.Empty<byte>());
    }
}
