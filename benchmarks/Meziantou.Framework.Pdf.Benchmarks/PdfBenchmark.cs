using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.Pdf.Benchmarks;

/// <summary>Reads, extracts the text of, writes and creates documents with Meziantou.Framework.Pdf.</summary>
/// <remarks>
/// The document is created by the library: pages of text in two standard fonts, shown with <c>Tj</c> and with <c>TJ</c>
/// arrays of kerning adjustments, and rectangles in colors, so that the content has the names, the integers, the reals,
/// the strings and the arrays of the content of a real document.
/// </remarks>
[MemoryDiagnoser]
public class PdfBenchmark
{
    private const string Words = "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat.";

    private byte[] _data = [];
    private PdfDocument _document = null!;

    [Params(1, 20)]
    public int PageCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        using var stream = new MemoryStream();
        CreateDocument(PageCount).Save(stream);
        _data = stream.ToArray();
        _document = PdfDocument.Open(_data);
    }

    [Benchmark]
    public int Open()
    {
        var document = PdfDocument.Open(_data);
        var count = 0;
        foreach (var page in document.Pages)
        {
            count += (int)page.MediaBox.Width;
        }

        return count;
    }

    [Benchmark]
    public int ExtractText()
    {
        var length = 0;
        foreach (var page in _document.Pages)
        {
            length += page.ExtractText().Length;
        }

        return length;
    }

    [Benchmark]
    public int ExtractTextLayout()
    {
        var count = 0;
        foreach (var page in _document.Pages)
        {
            count += page.ExtractTextLayout().Lines.Count;
        }

        return count;
    }

    [Benchmark]
    public long Save()
    {
        using var stream = new MemoryStream(_data.Length * 2);
        PdfDocument.Open(_data).Save(stream);
        return stream.Length;
    }

    [Benchmark]
    public long Create()
    {
        using var stream = new MemoryStream();
        CreateDocument(PageCount).Save(stream);
        return stream.Length;
    }

    private static PdfDocument CreateDocument(int pageCount)
    {
        var document = new PdfDocument();
        var regular = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);
        var bold = PdfFont.GetStandardFont(PdfStandardFont.TimesBold);
        var words = Words.Split(' ');
        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var page = document.Pages.Add(PdfSize.A4);
            var builder = page.CreateContentBuilder();
            for (var line = 0; line < 70; line++)
            {
                var y = 800 - (line * 11.25);
                builder.SetFillRgb(0.95, 0.9 - (line % 7 * 0.05), 0.85).Rectangle(36, y - 2.5, 523.28, 10.5).Fill().SetFillGray(0.1);
                if (line % 3 == 0)
                {
                    builder.DrawText(string.Join(' ', words, line % 5, 12), bold, 9.5, 40, y);
                }
                else
                {
                    // Kerned text: a TJ array of words and adjustments
                    var segments = new PdfTextSegment[16];
                    for (var i = 0; i < segments.Length; i += 2)
                    {
                        segments[i] = words[(line + i) % words.Length] + " ";
                        segments[i + 1] = -12.5 - (i % 4);
                    }

                    builder.BeginText().SetFont(regular, 9).MoveTextPosition(40.5, y).ShowText(segments).EndText();
                }
            }
        }

        return document;
    }
}
