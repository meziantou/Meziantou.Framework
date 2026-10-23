using System.Text;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Tests;

public sealed class PdfReaderTests
{
    [Fact]
    public void ReadsCrossReferenceTable()
    {
        var document = PdfDocument.Open(HandWrittenPdf.CreateSimple().ToArray());

        Assert.False(document.IsRepaired);
        Assert.Equal(PdfVersion.Pdf17, document.Version);
        Assert.Single(document.Pages);
        Assert.Equal(new PdfRectangle(0, 0, 612, 792), document.Pages[0].MediaBox);
        Assert.Equal("Hello", document.Pages[0].ExtractText());
    }

    [Fact]
    public void ReadsCrossReferenceStream()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.UseXrefStream = true;

        var document = PdfDocument.Open(file.ToArray());

        Assert.False(document.IsRepaired);
        Assert.Equal("Hello", document.Pages[0].ExtractText());
    }

    [Fact]
    public void ReadsObjectStreams()
    {
        var file = HandWrittenPdf.CreateSimple().Compress(1, 2, 3, 4);
        file.UseXrefStream = true;

        var document = PdfDocument.Open(file.ToArray());

        Assert.False(document.IsRepaired);
        Assert.Equal("Hello", document.Pages[0].ExtractText());
        Assert.True(document.Catalog.IsType(PdfName.Create("Catalog")));
    }

    [Fact]
    public void ReadsIncrementalUpdates()
    {
        var original = HandWrittenPdf.CreateSimple("Old").ToArray();
        var updated = HandWrittenPdf.AppendUpdate(original, [(5, HandWrittenPdf.Stream("BT /F1 12 Tf 72 720 Td (New) Tj ET")), (6, "<< /Title (Updated) >>")], "<< /Size 7 /Root 1 0 R /Info 6 0 R /Prev {prev} >>");

        var document = PdfDocument.Open(updated);

        Assert.False(document.IsRepaired);
        Assert.Equal("New", document.Pages[0].ExtractText());
        Assert.Equal("Updated", document.Information.Title);
    }

    [Fact]
    public void UpdateWithoutRootInheritsIt()
    {
        var original = HandWrittenPdf.CreateSimple().ToArray();
        var updated = HandWrittenPdf.AppendUpdate(original, [(6, "<< /Title (T) >>")], "<< /Size 7 /Info 6 0 R /Prev {prev} >>");

        var document = PdfDocument.Open(updated);

        Assert.Single(document.Pages);
        Assert.Equal("T", document.Information.Title);
    }

    [Fact]
    public void WrongOffsetsAreRecovered()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.OffsetError = 3;

        var document = PdfDocument.Open(file.ToArray());

        Assert.Equal("Hello", document.Pages[0].ExtractText());
    }

    [Fact]
    public void MissingCrossReferenceIsRebuilt()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.WriteXref = false;
        var bytes = Encoding.Latin1.GetString(file.ToArray()) + "trailer\n<< /Root 1 0 R >>\n";

        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(bytes));

        Assert.True(document.IsRepaired);
        Assert.Equal("Hello", document.Pages[0].ExtractText());
    }

    [Fact]
    public void MissingTrailerFindsTheCatalog()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.WriteXref = false;

        var document = PdfDocument.Open(file.ToArray());

        Assert.True(document.IsRepaired);
        Assert.Single(document.Pages);
    }

    [Fact]
    public void BrokenStartXrefIsRebuilt()
    {
        var text = Encoding.Latin1.GetString(HandWrittenPdf.CreateSimple().ToArray());
        text = text.Replace("startxref\n", "startxref\n9", StringComparison.Ordinal);

        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.True(document.IsRepaired);
        Assert.Equal("Hello", document.Pages[0].ExtractText());
    }

    [Fact]
    public void GarbageBeforeTheHeaderShiftsTheOffsets()
    {
        var file = HandWrittenPdf.CreateSimple().ToArray();
        var shifted = Encoding.ASCII.GetBytes("garbage\n").Concat(file).ToArray();

        var document = PdfDocument.Open(shifted);

        Assert.Equal("Hello", document.Pages[0].ExtractText());
    }

    [Fact]
    public void LaterDefinitionOfAnObjectWinsInRecovery()
    {
        var text = Encoding.Latin1.GetString(HandWrittenPdf.CreateSimple("First").ToArray());
        text = text[..text.IndexOf("xref", StringComparison.Ordinal)] + "5 0 obj\n" + HandWrittenPdf.Stream("BT /F1 12 Tf 72 720 Td (Second) Tj ET") + "\nendobj\n";

        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.True(document.IsRepaired);
        Assert.Equal("Second", document.Pages[0].ExtractText());
    }

    [Fact]
    public void TableThatStartsAtOneIsShifted()
    {
        var text = Encoding.Latin1.GetString(HandWrittenPdf.CreateSimple().ToArray());
        text = text.Replace("xref\n0 6\n", "xref\n1 6\n", StringComparison.Ordinal);

        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.False(document.IsRepaired);
        Assert.Equal("Hello", document.Pages[0].ExtractText());
    }

    [Fact]
    public void HeaderVersion()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.Header = "%PDF-1.4\n%\u00E2\u00E3\u00CF\u00D3\n";

        Assert.Equal(PdfVersion.Pdf14, PdfDocument.Open(file.ToArray()).Version);
    }

    [Fact]
    public void CatalogVersionOverridesTheHeader()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /Version /2.0 >>")
            .Add(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Header = "%PDF-1.4\n";

        Assert.Equal(PdfVersion.Pdf20, PdfDocument.Open(file.ToArray()).Version);
    }

    [Fact]
    public void NotAPdfThrows()
    {
        Assert.Throws<InvalidPdfContentException>(() => PdfDocument.Open("not a pdf"u8.ToArray()));
        Assert.Throws<InvalidPdfContentException>(() => PdfDocument.Open("%PDF-1.7\nnothing"u8.ToArray()));
    }

    [Fact]
    public void EncryptedDocumentsAreNotSupportedYet()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.Trailer = "<< /Size {size} /Root 1 0 R /Encrypt << /Filter /Standard /V 1 /R 2 >> >>";

        Assert.Throws<UnsupportedPdfFeatureException>(() => PdfDocument.Open(file.ToArray()));
    }

    [Fact]
    public void ReferencesToMissingObjectsAreNull()
    {
        var document = PdfDocument.Open(HandWrittenPdf.CreateSimple().ToArray());
        Assert.True(document.Objects.TryGetReference(1, out var catalog));

        Assert.Same(PdfNull.Instance, document.Objects.Resolve(document.Objects.GetOrCreateReference(99, 0)));
        Assert.IsType<PdfDictionary>(catalog.Resolve());
    }

    [Fact]
    public void PageTreeCyclesAndInheritance()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R 4 0 R 2 0 R] /Count 2 /MediaBox [0 0 200 300] /Rotate 90 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R >>")
            .Add(4, "<< /Parent 2 0 R /MediaBox [0 0 100 100] /Rotate -90 >>");

        var document = PdfDocument.Open(file.ToArray());

        Assert.Equal(2, document.Pages.Count);
        Assert.Equal(new PdfRectangle(0, 0, 200, 300), document.Pages[0].MediaBox);
        Assert.Equal(90, document.Pages[0].Rotation);
        Assert.Equal(new PdfRectangle(0, 0, 100, 100), document.Pages[1].MediaBox);
        Assert.Equal(270, document.Pages[1].Rotation);
        Assert.Equal(document.Pages[0].MediaBox, document.Pages[0].CropBox);
    }

    [Fact]
    public void StreamLengthThatIsAnIndirectObject()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Contents 4 0 R /Resources << /Font << /F1 6 0 R >> >> >>")
            .Add(4, "<< /Length 5 0 R >>\nstream\nBT /F1 1 Tf (A) Tj ET\nendstream")
            .Add(5, "21")
            .Add(6, "<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>");

        var document = PdfDocument.Open(file.ToArray());

        Assert.Equal("BT /F1 1 Tf (A) Tj ET", Encoding.ASCII.GetString(document.Pages[0].GetContentData()));
        Assert.Equal("A", document.Pages[0].ExtractText());
    }

    [Fact]
    public void SelfReferencingLengthIsNotACycle()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>")
            .Add(4, "<< /Length 4 0 R >>\nstream\nq Q\nendstream");

        var document = PdfDocument.Open(file.ToArray());

        Assert.Equal("q Q", Encoding.ASCII.GetString(document.Pages[0].GetContentData()));
    }
}
