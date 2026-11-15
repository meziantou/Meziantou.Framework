using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.Typography;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// The outlines of a PostScript Type 1 font, drawn by the library, compared with the ones FreeType gives: MuPDF
/// (<c>mutool draw -F svg -O text=path</c>) draws the font embedded in a PDF file and writes the outline of each glyph
/// as an SVG path, in ems.
/// </summary>
public sealed class Type1FontInteropTests
{
    // FreeType keeps the points in 16.16 and MuPDF writes them in ems as single-precision numbers: a point moves by
    // a hundredth of a unit. FreeType also moves a point of the glyph "x" by 0.38 unit (the hint replacement of the glyph).
    private const double Tolerance = 0.5;

    // The glyphs of the sample font, by their code in the PDF file (the encoding of the font, and two more codes) and
    // their character: lines, the accented characters of seac (Aring has a side bearing that differs from the one of
    // its base), curves, hint replacement, flex, sbw, stem hints and div
    private static readonly (int Code, string Text)[] Glyphs =
    [
        (65, "A"), (193, "\u00C1"), (79, "O"), (197, "\u00C5"), (120, "x"), (69, "E"), (46, "."), (72, "H"), (200, "\uFB01"),
    ];

    [Fact]
    public async Task TheOutlinesOfAType1FontAreTheOnesOfFreeType()
    {
        var mutool = await PrebuiltTools.Mutool.GetOrDownloadAsync(XunitCancellationToken);
        var directory = InteropSettings.GetArtifactsDirectory(nameof(Type1FontInteropTests));
        Directory.CreateDirectory(directory);
        var pdfPath = directory / "type1.pdf";
        var svgPath = directory / "type1.svg";
        var writer = FontType1Writer.Sample();
        await File.WriteAllBytesAsync(pdfPath, CreatePdf(writer.WritePfb()), XunitCancellationToken);
        var result = await ProcessRunner.RunAsync(mutool, ["draw", "-q", "-F", "svg", "-O", "text=path", "-o", svgPath, pdfPath, "1"], XunitCancellationToken);
        if (result.ExitCode != 0)
        {
            Assert.Fail($"mutool draw failed (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}");
        }

        // The glyphs are symbols, used in the order they are drawn; the font is drawn at 1 point per unit (1000 per em)
        var svg = XDocument.Load(svgPath);
        XNamespace svgNamespace = "http://www.w3.org/2000/svg";
        XNamespace xlink = "http://www.w3.org/1999/xlink";
        var symbols = svg.Descendants(svgNamespace + "path").Where(path => path.Attribute("id") is not null).ToDictionary(path => (string)path.Attribute("id")!, path => (string)path.Attribute("d")!, StringComparer.Ordinal);
        var uses = svg.Descendants(svgNamespace + "use").ToList();
        Assert.HasCount(Glyphs.Length, uses);
        var expected = uses.Select(use => VectorPath.ParseSvgPathData(symbols[((string)use.Attribute(xlink + "href")!).TrimStart('#')]).Transform(AffineTransform.CreateScale(1000, -1000))).ToList();
        foreach (var font in new[] { Font.Load(writer.WritePfb()), Font.Load(writer.WritePfa()) })
        {
            for (var i = 0; i < Glyphs.Length; i++)
            {
                // At 1000 pixels per em, a pixel is a unit of the font; the baseline is at 0, y down
                var layout = TextLayout.Create(Glyphs[i].Text, new TextStyle(font, 1000));
                var actual = layout.ToPath(new PointD(0, -layout.Baseline));
                AssertSameOutline(Glyphs[i].Text, expected[i], actual);
            }
        }
    }

    private static void AssertSameOutline(string character, VectorPath expected, VectorPath actual)
    {
        // MuPDF does not close a contour that another one follows; every contour ends with a line to its start
        var expectedSegments = expected.EnumerateSegments().Where(segment => segment.Kind != PathSegmentKind.Close).ToList();
        var actualSegments = actual.EnumerateSegments().Where(segment => segment.Kind != PathSegmentKind.Close).ToList();
        var description = string.Create(CultureInfo.InvariantCulture, $"{character}: FreeType {expected.ToSvgPathData()}, library {actual.ToSvgPathData()}");
        if (expectedSegments.Count != actualSegments.Count)
        {
            Assert.Fail(description);
        }

        for (var i = 0; i < expectedSegments.Count; i++)
        {
            var (e, a) = (expectedSegments[i], actualSegments[i]);
            if (e.Kind != a.Kind)
            {
                Assert.Fail(description);
            }

            Assert.True(IsClose(e.Point, a.Point), description);
            if (e.Kind == PathSegmentKind.CubicBezierTo)
            {
                Assert.True(IsClose(e.Control1, a.Control1) && IsClose(e.Control2, a.Control2), description);
            }
        }

        static bool IsClose(PointD expected, PointD actual) => Math.Abs(expected.X - actual.X) <= Tolerance && Math.Abs(expected.Y - actual.Y) <= Tolerance;
    }

    /// <summary>
    /// Writes a PDF file of one page that draws each glyph of <see cref="Glyphs"/> with a Type 1 font embedded from a
    /// PFB file (ISO 32000-1, 9.9: the clear text, the encrypted part and the trailer of the program, their lengths in
    /// <c>Length1</c>, <c>Length2</c> and <c>Length3</c>), 1000 points per em, a glyph every 1500 points.
    /// </summary>
    private static byte[] CreatePdf(byte[] pfb)
    {
        // The segments of a PFB file: 0x80, the type, the length (little endian), the data; type 3 ends the file
        var segments = new List<byte[]>();
        for (var offset = 0; pfb[offset] == 0x80 && pfb[offset + 1] != 3;)
        {
            var length = BitConverter.ToInt32(pfb, offset + 2);
            segments.Add(pfb[(offset + 6)..(offset + 6 + length)]);
            offset += 6 + length;
        }

        Assert.HasCount(3, segments);
        var content = new StringBuilder();
        var encoding = Encoding.Latin1;
        for (var i = 0; i < Glyphs.Length; i++)
        {
            content.Append(CultureInfo.InvariantCulture, $"BT /F1 1000 Tf {1500 * i} 2000 Td <{Glyphs[i].Code:X2}> Tj ET\n");
        }

        byte[][] objects =
        [
            encoding.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"),
            encoding.GetBytes("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            encoding.GetBytes(string.Create(CultureInfo.InvariantCulture, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {1500 * Glyphs.Length} 3000] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>")),
            Stream(string.Create(CultureInfo.InvariantCulture, $"<< /Length {content.Length} >>"), encoding.GetBytes(content.ToString())),
            encoding.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /MeziantouType1-Regular /Encoding << /Differences [197 /Aring 200 /fi] >> /FontDescriptor 6 0 R >>"),
            encoding.GetBytes("<< /Type /FontDescriptor /FontName /MeziantouType1-Regular /Flags 32 /FontBBox [-50 -250 1000 900] /ItalicAngle 0 /Ascent 900 /Descent -250 /CapHeight 700 /StemV 80 /FontFile 7 0 R >>"),
            Stream(string.Create(CultureInfo.InvariantCulture, $"<< /Length {segments.Sum(segment => segment.Length)} /Length1 {segments[0].Length} /Length2 {segments[1].Length} /Length3 {segments[2].Length} >>"), [.. segments[0], .. segments[1], .. segments[2]]),
        ];

        using var output = new MemoryStream();
        output.Write(encoding.GetBytes("%PDF-1.4\n"));
        var offsets = new List<long>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(output.Position);
            output.Write(encoding.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n")));
            output.Write(objects[i]);
            output.Write(encoding.GetBytes("\nendobj\n"));
        }

        var xref = output.Position;
        var trailer = new StringBuilder();
        trailer.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            trailer.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        trailer.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        output.Write(encoding.GetBytes(trailer.ToString()));
        return output.ToArray();

        byte[] Stream(string dictionary, byte[] data) => [.. encoding.GetBytes(dictionary + "\nstream\n"), .. data, .. encoding.GetBytes("\nendstream")];
    }
}
