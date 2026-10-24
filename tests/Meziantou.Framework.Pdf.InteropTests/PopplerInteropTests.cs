using Meziantou.Framework.Pdf.TestHarness.ExternalTools;

namespace Meziantou.Framework.Pdf.InteropTests;

/// <summary>Poppler reads the files the library writes: their text, their page count and their metadata.</summary>
public sealed class PopplerInteropTests
{
    public static TheoryData<string, bool> Written => InteropDocuments.Written;

    public static TheoryData<string> ValidFixtures => InteropDocuments.ValidFixtures;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TextIsExtractedByPdftotext(bool objectStreams)
    {
        var poppler = await PopplerTool.LocateAsync(XunitCancellationToken);
        var path = InteropDocuments.Write("text", objectStreams);

        var text = await poppler.ExtractTextAsync(path, XunitCancellationToken);

        Assert.Equal(string.Join("\n\n", InteropDocuments.GetExpectedText("text")), Normalize(text));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmbeddedFontsAreReadByPoppler(bool objectStreams)
    {
        var poppler = await PopplerTool.LocateAsync(XunitCancellationToken);
        var path = InteropDocuments.Write("fonts", objectStreams);

        var fonts = await poppler.GetFontsAsync(path, XunitCancellationToken);
        var text = await poppler.ExtractTextAsync(path, XunitCancellationToken);

        // name, type, encoding, emb, sub, uni, object, generation: each font is an embedded subset with a ToUnicode CMap
        Assert.Equal(InteropDocuments.EmbeddedFonts.Length, fonts.Count);
        Assert.All(fonts, line => Assert.Matches("^[A-Z]{6}\\+\\S+ +CID (TrueType|Type 0C) +Identity-H +yes yes yes ", line));
        var expected = InteropDocuments.GetExpectedText("fonts");
        var pages = Normalize(text).Split("\n\n");
        Assert.HasCount(expected.Length, pages);
        for (var i = 0; i < expected.Length; i++)
        {
            if (expected[i].Any(c => c is >= (char)0x0600 and <= (char)0x06FF))
            {
                // pdftotext writes the right-to-left lines in an order of its own, between directional formatting
                // characters, whether the text is a replacement text or the text of each glyph: only the characters are compared
                Assert.Equal(expected[i].Order(), pages[i].Where(c => char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).Order());
            }
            else
            {
                Assert.Equal(expected[i], pages[i]);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Written))]
    public async Task PdfinfoReadsTheDocument(string name, bool objectStreams)
    {
        var poppler = await PopplerTool.LocateAsync(XunitCancellationToken);
        var document = InteropDocuments.Create(name);
        var path = InteropDocuments.Write(name, objectStreams);

        var info = await poppler.GetInfoAsync(path, XunitCancellationToken);

        Assert.Equal(document.Pages.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), info["Pages"]);
        if (document.Information.Title is { } title)
        {
            Assert.Equal(title, info["Title"]);
        }
    }

    [Theory]
    [MemberData(nameof(ValidFixtures))]
    public async Task TextOfTheFixturesMatchesPdftotext(string fixture)
    {
        var poppler = await PopplerTool.LocateAsync(XunitCancellationToken);
        var path = InteropDocuments.GetFixturePath(fixture);
        var document = PdfDocument.Open(File.ReadAllBytes(path));

        var expected = Normalize(await poppler.ExtractTextAsync(path, XunitCancellationToken));
        var actual = Normalize(string.Join("\n\n", document.Pages.Select(page => page.ExtractText())));

        // pdftotext writes the ligatures as their letters
        Assert.Equal(expected, actual.Replace("\uFB01", "fi", StringComparison.Ordinal).Replace("\uFB02", "fl", StringComparison.Ordinal));
    }

    /// <summary>Removes the form feeds, the trailing whitespace of the lines and the empty lines at the end.</summary>
    private static string Normalize(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\f', '\n').Split('\n').Select(line => line.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join('\n', lines).Trim('\n');
    }
}
