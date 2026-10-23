using System.Text;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.SnapshotTesting;

namespace Meziantou.Framework.Pdf.Tests;

public sealed class PdfWriterTests
{
    private static readonly PdfSaveOptions Uncompressed = new() { CompressStreams = false, UseObjectStreams = false };

    [Fact]
    public void GoldenMinimalDocument()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(200, 100));
        page.CreateContentBuilder()
            .SetFillRgb(1, 0, 0)
            .Rectangle(10, 10, 50, 30)
            .Fill()
            .DrawText("Hello", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 12, 20, 60);

        Snapshot.Validate(Encoding.Latin1.GetString(document.ToByteArray(Uncompressed)));
    }

    [Fact]
    public void GoldenObjectStreams()
    {
        var document = new PdfDocument();
        document.Pages.Add(new PdfSize(200, 100)).CreateContentBuilder().MoveTo(0, 0).LineTo(10, 10).Stroke();

        var bytes = document.ToByteArray(new PdfSaveOptions { CompressStreams = false, UseObjectStreams = true });

        Snapshot.Validate(bytes, SnapshotType.Create("pdf"));
    }

    [Fact]
    public void SavingIsDeterministic()
    {
        static byte[] Create()
        {
            var document = new PdfDocument();
            document.Information.Title = "Same";
            document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Same", PdfFont.GetStandardFont(PdfStandardFont.TimesRoman), 10, 10, 10);
            return document.ToByteArray();
        }

        Assert.Equal(Create(), Create());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RoundTrip(bool compress, bool objectStreams)
    {
        var document = CreateRichDocument();

        var saved = document.ToByteArray(new PdfSaveOptions { CompressStreams = compress, UseObjectStreams = objectStreams });
        var read = PdfDocument.Open(saved);

        Assert.False(read.IsRepaired);
        Assert.Equal(PdfVersion.Pdf17, read.Version);
        Assert.Equal(3, read.Pages.Count);
        Assert.Equal("Title \u00E9\u4E2D", read.Information.Title);
        Assert.Equal("Author", read.Information.Author);
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(-5)), read.Information.CreationDate);
        Assert.Equal("Page 1\nSecond line", read.Pages[0].ExtractText());
        Assert.Equal("Page 2", read.Pages[1].ExtractText());
        Assert.Equal(PdfSize.Letter.Width, read.Pages[2].MediaBox.Width);
        Assert.Equal(90, read.Pages[2].Rotation);

        Assert.HasCount(2, read.Outlines);
        Assert.Equal("Chapter 1", read.Outlines[0].Title);
        Assert.Same(read.Pages[0], read.Outlines[0].Destination!.Page);
        Assert.Single(read.Outlines[0].Children);
        Assert.Equal("Section 1.1", read.Outlines[0].Children[0].Title);
        Assert.Equal(PdfDestinationKind.Xyz, read.Outlines[0].Children[0].Destination!.Kind);
        Assert.Equal(700, read.Outlines[0].Children[0].Destination!.Top);
        Assert.True(read.Outlines[0].IsOpen);
        Assert.True(read.Outlines[1].IsBold);
        Assert.Equal(PdfColor.FromRgb(1, 0, 0), read.Outlines[1].Color);

        var links = read.Pages[0].Annotations.OfType<PdfLinkAnnotation>().ToList();
        Assert.HasCount(2, links);
        Assert.Equal(new Uri("https://www.meziantou.net/"), links[0].Uri);
        Assert.Same(read.Pages[1], links[1].Destination!.Page);
        Assert.Equal(new PdfRectangle(10, 20, 110, 40), links[0].Rectangle);
    }

    [Fact]
    public void RoundTripKeepsTheObjectGraph()
    {
        var document = CreateRichDocument();
        var first = document.ToByteArray(Uncompressed);

        var second = PdfDocument.Open(first).ToByteArray(Uncompressed);

        // A document written by the library and read back is written with the same objects; only the /ID differs
        Assert.Equal(RemoveId(first), RemoveId(second));
    }

    [Fact]
    public void UnreachableObjectsAreNotWritten()
    {
        var document = new PdfDocument();
        document.Objects.Add(new PdfDictionary { [PdfName.Create("Orphan")] = PdfBoolean.True });
        document.Pages.Add(PdfSize.A5);

        var text = Encoding.Latin1.GetString(document.ToByteArray(Uncompressed));

        Assert.DoesNotContain("Orphan", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovedPagesAreNotWritten()
    {
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A5).CreateContentBuilder().DrawText("Kept", PdfFont.GetStandardFont(PdfStandardFont.Courier), 10, 10, 10);
        document.Pages.Add(PdfSize.A5).CreateContentBuilder().DrawText("Removed", PdfFont.GetStandardFont(PdfStandardFont.Courier), 10, 10, 10);
        document.Pages.RemoveAt(1);

        var read = PdfDocument.Open(document.ToByteArray(Uncompressed));

        Assert.Single(read.Pages);
        Assert.Equal("Kept", read.Pages[0].ExtractText());
        Assert.DoesNotContain("Removed", Encoding.Latin1.GetString(document.ToByteArray(Uncompressed)), StringComparison.Ordinal);
    }

    [Fact]
    public void IncrementalSaveAppendsTheChanges()
    {
        var original = HandWrittenPdf.CreateSimple().ToArray();
        var document = PdfDocument.Open(original);
        document.Information.Title = "Changed";
        document.Pages[0].CreateContentBuilder().DrawText("World", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 12, 72, 700);

        using var output = new MemoryStream();
        document.SaveIncremental(output, Uncompressed);
        var updated = output.ToArray();

        Assert.Equal(original, updated.AsSpan(0, original.Length).ToArray());
        var appended = Encoding.Latin1.GetString(updated.AsSpan(original.Length));
        Assert.Contains("/Prev ", appended, StringComparison.Ordinal);
        Assert.DoesNotContain("\n4 0 obj", appended, StringComparison.Ordinal);
        Assert.StartsWith("3 0 obj", appended, StringComparison.Ordinal);

        var read = PdfDocument.Open(updated);
        Assert.False(read.IsRepaired);
        Assert.Equal("Changed", read.Information.Title);
        Assert.Equal("Hello\nWorld", read.Pages[0].ExtractText());
    }

    [Fact]
    public void IncrementalSaveOfAnUnchangedDocumentWritesNoObject()
    {
        var original = HandWrittenPdf.CreateSimple().ToArray();
        var document = PdfDocument.Open(original);
        _ = document.Pages[0].ExtractText();

        using var output = new MemoryStream();
        document.SaveIncremental(output);
        var appended = Encoding.Latin1.GetString(output.ToArray().AsSpan(original.Length));

        Assert.DoesNotContain(" obj", appended, StringComparison.Ordinal);
        Assert.StartsWith("xref\ntrailer", appended, StringComparison.Ordinal);
    }

    [Fact]
    public void IncrementalSaveOfACrossReferenceStream()
    {
        var file = HandWrittenPdf.CreateSimple().Compress(1, 2, 4);
        file.UseXrefStream = true;
        var original = file.ToArray();
        var document = PdfDocument.Open(original);
        document.Information.Author = "Me";

        using var output = new MemoryStream();
        document.SaveIncremental(output, Uncompressed);

        var appended = Encoding.Latin1.GetString(output.ToArray().AsSpan(original.Length));
        Assert.Contains("/Type /XRef", appended, StringComparison.Ordinal);
        var read = PdfDocument.Open(output.ToArray());
        Assert.False(read.IsRepaired);
        Assert.Equal("Me", read.Information.Author);
        Assert.Equal("Hello", read.Pages[0].ExtractText());
    }

    [Fact]
    public void IncrementalSaveNeedsAFile()
    {
        using var output = new MemoryStream();

        Assert.Throws<InvalidOperationException>(() => new PdfDocument().SaveIncremental(output));
    }

    [Fact]
    public void IncrementalSaveOfARepairedFileThrows()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.WriteXref = false;
        var document = PdfDocument.Open(file.ToArray());

        using var output = new MemoryStream();
        Assert.Throws<InvalidOperationException>(() => document.SaveIncremental(output));
    }

    [Fact]
    public void RepairedFileCanBeSavedInFull()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.OffsetError = 7;
        file.WriteXref = false;
        var document = PdfDocument.Open(file.ToArray());

        var read = PdfDocument.Open(document.ToByteArray());

        Assert.False(read.IsRepaired);
        Assert.Equal("Hello", read.Pages[0].ExtractText());
    }

    [Fact]
    public void ObjectStreamsNeedPdf15()
    {
        var document = new PdfDocument { Version = PdfVersion.Pdf14 };

        Assert.StartsWith("%PDF-1.5", Encoding.ASCII.GetString(document.ToByteArray(new PdfSaveOptions { UseObjectStreams = true })), StringComparison.Ordinal);
        Assert.StartsWith("%PDF-1.4", Encoding.ASCII.GetString(document.ToByteArray(new PdfSaveOptions { UseObjectStreams = false })), StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => document.ToByteArray(new PdfSaveOptions { UseObjectStreams = true, Version = PdfVersion.Pdf14 }));
    }

    [Fact]
    public void FileIdentifierKeepsItsFirstPart()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.Trailer = "<< /Size {size} /Root 1 0 R /ID [<0102> <0304>] >>";

        var saved = PdfDocument.Open(file.ToArray()).ToByteArray();
        var id = PdfDocument.Open(saved).Trailer.Get<PdfArray>(PdfName.Create("ID"))!;

        Assert.Equal(new byte[] { 1, 2 }, id.GetResolved(0) is PdfString first ? first.Bytes.ToArray() : null);
        Assert.Equal(16, ((PdfString)id.GetResolved(1)).Bytes.Length);
    }

    internal static PdfDocument CreateRichDocument()
    {
        var document = new PdfDocument();
        document.Information.Title = "Title \u00E9\u4E2D";
        document.Information.Author = "Author";
        document.Information.CreationDate = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(-5));
        var helvetica = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);
        var page1 = document.Pages.Add(PdfSize.A4);
        page1.CreateContentBuilder()
            .BeginText()
            .SetFont(helvetica, 12)
            .SetTextLeading(14)
            .MoveTextPosition(72, 750)
            .ShowText("Page 1")
            .ShowTextOnNextLine("Second line")
            .EndText();
        var page2 = document.Pages.Add(PdfSize.A4);
        page2.CreateContentBuilder().DrawText("Page 2", PdfFont.GetStandardFont(PdfStandardFont.TimesBold), 20, 100, 700);
        var page3 = document.Pages.Add(PdfSize.Letter);
        page3.Rotation = 90;

        var chapter = document.Outlines.Add("Chapter 1", PdfDestination.Fit(page1));
        chapter.IsOpen = true;
        chapter.Children.Add("Section 1.1", PdfDestination.Xyz(page1, 0, 700));
        var chapter2 = document.Outlines.Add("Chapter 2", PdfDestination.FitHorizontally(page2, 500));
        chapter2.IsBold = true;
        chapter2.Color = PdfColor.FromRgb(1, 0, 0);

        page1.Annotations.AddLink(new PdfRectangle(10, 20, 110, 40), new Uri("https://www.meziantou.net/"));
        page1.Annotations.AddLink(new PdfRectangle(10, 50, 110, 70), PdfDestination.Fit(page2));
        return document;
    }

    private static string RemoveId(byte[] file)
    {
        var text = Encoding.Latin1.GetString(file);
        var index = text.IndexOf("/ID [", StringComparison.Ordinal);
        return text[..index] + text[text.IndexOf(']', index, StringComparison.Ordinal)..];
    }
}
