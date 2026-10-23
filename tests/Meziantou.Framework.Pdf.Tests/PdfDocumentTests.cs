using System.Text;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Tests;

public sealed class PdfDocumentTests
{
    [Fact]
    public void Example_CreateDocument()
    {
        #region Pdf_CreateDocument
        var document = new PdfDocument();
        document.Information.Title = "Invoice";
        var font = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);

        var page = document.Pages.Add(PdfSize.A4);
        page.CreateContentBuilder()
            .SetFillRgb(0.2, 0.4, 0.8)
            .Rectangle(50, 750, 200, 40)
            .Fill()
            .SetFillGray(0)
            .DrawText("Hello, PDF!", font, 24, 60, 700);

        using var stream = new MemoryStream();
        document.Save(stream);
        #endregion

        var read = PdfDocument.Open(stream.ToArray());
        Assert.Equal("Invoice", read.Information.Title);
        Assert.Equal("Hello, PDF!", read.Pages[0].ExtractText());
    }

    [Fact]
    public void Example_EmbeddedFont()
    {
        var fontData = File.ReadAllBytes(TestHarness.RepositoryPaths.Fonts / "noto-sans" / "NotoSans-Regular.subset.ttf");

        #region Pdf_EmbeddedFont
        // A .ttf, .otf, .ttc, .woff or .woff2 file
        var font = PdfFont.Load(fontData);

        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4).CreateContentBuilder()
            .DrawText("Office \u2014 AVATAR", font, 24, 60, 700);

        // The document embeds the glyphs it shows, with their text
        var pdf = document.ToByteArray();
        #endregion

        Assert.Equal("Office \u2014 AVATAR", PdfDocument.Open(pdf).Pages[0].ExtractText());
    }

    [Fact]
    public void Example_AdvancedGraphics()
    {
        #region Pdf_AdvancedGraphics
        var document = new PdfDocument();
        var builder = document.Pages.Add(PdfSize.A4).CreateContentBuilder();

        // A gradient from red to blue, painted in a rectangle
        var gradient = PdfShading.Axial(PdfDeviceColorSpace.Rgb, 50, 0, 250, 0,
            PdfFunction.Gradient((0, PdfColor.FromRgb(1, 0, 0)), (1, PdfColor.FromRgb(0, 0, 1))));
        builder.SaveGraphicsState()
            .Rectangle(50, 600, 200, 100).Clip().EndPath()
            .PaintShading(gradient)
            .RestoreGraphicsState();

        // A form painted twice, the second time at half opacity with the Multiply blend mode
        var logo = document.CreateForm(new PdfRectangle(0, 0, 100, 100));
        logo.CreateContentBuilder().SetFillRgb(1, 0.8, 0).Circle(50, 50, 50).Fill();
        builder.DrawForm(logo)
            .SaveGraphicsState()
            .SetGraphicsState(new PdfExtGState { FillAlpha = 0.5, BlendMode = PdfBlendMode.Multiply })
            .Translate(50, 0)
            .DrawForm(logo)
            .RestoreGraphicsState();

        // A checkerboard: a tiling pattern used as the fill color
        var checkerboard = document.CreateTilingPattern(new PdfRectangle(0, 0, 20, 20), 20, 20);
        checkerboard.CreateContentBuilder().SetFillGray(0).Rectangle(0, 0, 10, 10).Rectangle(10, 10, 10, 10).Fill();
        builder.SetFillPattern(checkerboard).Rectangle(300, 600, 200, 100).Fill();
        #endregion

        var page = PdfDocument.Open(document.ToByteArray()).Pages[0];
        Assert.Equal(["Sh1"], page.Resources!.Get<PdfDictionary>(PdfNames.Shading)!.Keys.Select(key => key.Value));
        Assert.Equal(["Fm1"], page.Resources.Get<PdfDictionary>(PdfNames.XObject)!.Keys.Select(key => key.Value));
        Assert.Equal(["P1"], page.Resources.Get<PdfDictionary>(PdfNames.Pattern)!.Keys.Select(key => key.Value));
    }

    [Fact]
    public void Example_ReadDocument()
    {
        var file = HandWrittenPdf.CreateSimple("Some text").ToArray();

        #region Pdf_ReadDocument
        var document = PdfDocument.Open(file);
        var pageCount = document.Pages.Count;
        var text = document.Pages[0].ExtractText();
        #endregion

        Assert.Equal(1, pageCount);
        Assert.Equal("Some text", text);
    }

    [Fact]
    public void Example_IncrementalUpdate()
    {
        var file = HandWrittenPdf.CreateSimple().ToArray();

        #region Pdf_IncrementalUpdate
        var document = PdfDocument.Open(file);
        document.Information.Author = "Reviewer";

        using var output = new MemoryStream();
        document.SaveIncremental(output);
        #endregion

        Assert.Equal("Reviewer", PdfDocument.Open(output.ToArray()).Information.Author);
    }

    [Fact]
    public void Example_OutlineAndLinks()
    {
        #region Pdf_OutlineAndLinks
        var document = new PdfDocument();
        var cover = document.Pages.Add(PdfSize.A4);
        var content = document.Pages.Add(PdfSize.A4);

        var chapter = document.Outlines.Add("Chapter 1", PdfDestination.Fit(content));
        chapter.Children.Add("Section 1.1", PdfDestination.Xyz(content, left: 0, top: 500));

        cover.Annotations.AddLink(new PdfRectangle(50, 50, 250, 80), PdfDestination.Fit(content));
        cover.Annotations.AddLink(new PdfRectangle(50, 100, 250, 130), new Uri("https://www.meziantou.net"));
        #endregion

        var read = PdfDocument.Open(document.ToByteArray());
        Assert.Equal("Chapter 1", read.Outlines[0].Title);
        Assert.Equal(2, read.Pages[0].Annotations.Count);
    }

    [Fact]
    public void Example_LowLevelObjects()
    {
        #region Pdf_LowLevelObjects
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.Letter);

        // The catalog is a dictionary of the object model: entries the API has no member for can be set directly
        document.Catalog[PdfName.Create("PageLayout")] = PdfName.Create("TwoColumnLeft");
        var viewerPreferences = new PdfDictionary
        {
            [PdfName.Create("DisplayDocTitle")] = PdfBoolean.True,
        };
        document.Catalog[PdfName.Create("ViewerPreferences")] = document.Objects.Add(viewerPreferences);
        #endregion

        var read = PdfDocument.Open(document.ToByteArray());
        Assert.Equal("TwoColumnLeft", read.Catalog.GetName(PdfName.Create("PageLayout"))?.Value);
        Assert.True(read.Catalog.Get<PdfDictionary>(PdfName.Create("ViewerPreferences"))!.GetBoolean(PdfName.Create("DisplayDocTitle")));
    }

    [Fact]
    public void NewDocumentHasACatalogAndAnEmptyPageTree()
    {
        var document = new PdfDocument();

        Assert.Empty(document.Pages);
        Assert.True(document.Catalog.IsType(PdfName.Create("Catalog")));
        Assert.Equal(PdfVersion.Pdf17, document.Version);
        Assert.Empty(PdfDocument.Open(document.ToByteArray()).Pages);
    }

    [Fact]
    public void InsertAndRemovePages()
    {
        var document = new PdfDocument();
        var a = document.Pages.Add(new PdfSize(100, 100));
        var c = document.Pages.Add(new PdfSize(300, 300));
        var b = document.Pages.Insert(1, new PdfSize(200, 200));

        Assert.Equal([a, b, c], document.Pages);
        document.Pages.RemoveAt(0);

        var read = PdfDocument.Open(document.ToByteArray());
        Assert.Equal([200.0, 300.0], read.Pages.Select(p => p.MediaBox.Width));
        Assert.Equal(2, read.Catalog.Get<PdfDictionary>(PdfName.Create("Pages"))!.GetInteger(PdfName.Create("Count")));
    }

    [Fact]
    public void AddPageToADocumentRead()
    {
        var document = PdfDocument.Open(HandWrittenPdf.CreateSimple().ToArray());
        document.Pages.Add(PdfSize.A5).CreateContentBuilder().DrawText("Added", PdfFont.GetStandardFont(PdfStandardFont.Courier), 10, 10, 10);

        var read = PdfDocument.Open(document.ToByteArray());

        Assert.Equal(2, read.Pages.Count);
        Assert.Equal("Hello", read.Pages[0].ExtractText());
        Assert.Equal("Added", read.Pages[1].ExtractText());
    }

    [Fact]
    public void ContentAddedToAPageReadIsIsolated()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /Resources << /Font << /F1 5 0 R >> >> >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>")
            .Add(4, HandWrittenPdf.Stream("2 0 0 2 0 0 cm BT /F1 10 Tf (Old) Tj ET"))
            .Add(5, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        var document = PdfDocument.Open(file.ToArray());

        document.Pages[0].CreateContentBuilder().DrawText("New", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 10, 0, 100);

        var content = Encoding.ASCII.GetString(document.Pages[0].GetContentData());
        Assert.StartsWith("q\n\n2 0 0 2 0 0 cm", content, StringComparison.Ordinal);
        Assert.Contains("Q\nBT\n", content, StringComparison.Ordinal);

        // The inherited resources were copied to the page before the new font was added
        var resources = document.Pages[0].Dictionary.Get<PdfDictionary>(PdfName.Create("Resources"))!;
        Assert.Equal(2, resources.Get<PdfDictionary>(PdfName.Create("Font"))!.Count);
        Assert.Equal(1, document.Catalog.Get<PdfDictionary>(PdfName.Create("Pages"))!.Get<PdfDictionary>(PdfName.Create("Resources"))!.Get<PdfDictionary>(PdfName.Create("Font"))!.Count);
        Assert.Equal("Old\nNew", document.Pages[0].ExtractText());
    }

    [Fact]
    public void InformationDictionary()
    {
        var document = new PdfDocument();
        Assert.Null(document.Information.Dictionary);

        document.Information.Title = "T";
        document.Information.Subject = "S";
        document.Information.Keywords = "K";
        document.Information.Creator = "C";
        document.Information.Producer = "P";
        document.Information.ModificationDate = new DateTimeOffset(2020, 2, 29, 23, 59, 58, TimeSpan.Zero);
        document.Information.SetText("Custom", "Value");
        document.Information.Subject = null;

        var read = PdfDocument.Open(document.ToByteArray()).Information;
        Assert.Equal("T", read.Title);
        Assert.Null(read.Subject);
        Assert.Equal("K", read.Keywords);
        Assert.Equal("C", read.Creator);
        Assert.Equal("P", read.Producer);
        Assert.Equal(new DateTimeOffset(2020, 2, 29, 23, 59, 58, TimeSpan.Zero), read.ModificationDate);
        Assert.Equal("Value", read.GetText("Custom"));
        Assert.Equal("D:20200229235958Z", read.Dictionary!.GetText(PdfName.Create("ModDate")));
    }

    [Fact]
    public void XmpMetadataFromTheInformation()
    {
        var document = new PdfDocument();
        document.Information.Title = "A & B";
        document.Information.Author = "Me";
        document.Information.CreationDate = new DateTimeOffset(2021, 3, 4, 5, 6, 7, TimeSpan.FromHours(2));
        document.UpdateXmpMetadata();

        var read = PdfDocument.Open(document.ToByteArray());
        var xmp = Encoding.UTF8.GetString(read.GetXmpMetadata()!);

        Assert.StartsWith("<?xpacket begin=\"\uFEFF\"", xmp, StringComparison.Ordinal);
        Assert.Contains("<rdf:li xml:lang=\"x-default\">A &amp; B</rdf:li>", xmp, StringComparison.Ordinal);
        Assert.Contains("<rdf:li>Me</rdf:li>", xmp, StringComparison.Ordinal);
        Assert.Contains("<xmp:CreateDate>2021-03-04T05:06:07+02:00</xmp:CreateDate>", xmp, StringComparison.Ordinal);
        Assert.EndsWith("<?xpacket end=\"w\"?>", xmp, StringComparison.Ordinal);

        // The metadata stream is not compressed, so that tools that do not read PDF can find it
        Assert.Contains("<x:xmpmeta", Encoding.Latin1.GetString(document.ToByteArray()), StringComparison.Ordinal);
        read.RemoveXmpMetadata();
        Assert.Null(read.GetXmpMetadata());
    }

    [Fact]
    public void ImagesFromImaging()
    {
        using var image = Image.ImportPixelData<Rgba32>([new Rgba32(255, 0, 0, 255), new Rgba32(0, 255, 0, 128), new Rgba32(0, 0, 255, 0), new Rgba32(255, 255, 255, 255)], 2, 2);
        var pdfImage = PdfImage.FromImage(image);
        Assert.True(pdfImage.HasSoftMask);

        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A5).CreateContentBuilder().DrawImage(pdfImage, 10, 10, 100, 100);
        var read = PdfDocument.Open(document.ToByteArray());

        var xobject = read.Pages[0].Resources!.Get<PdfDictionary>(PdfName.Create("XObject"))!.Get<PdfStream>(PdfName.Create("Im1"))!;
        Assert.Equal([255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255], xobject.GetDecodedData());
        var mask = xobject.Dictionary.Get<PdfStream>(PdfName.Create("SMask"))!;
        Assert.Equal([255, 128, 0, 255], mask.GetDecodedData());
        Assert.Equal("DeviceGray", mask.Dictionary.GetName(PdfName.Create("ColorSpace"))!.Value);
        Assert.Contains("/Im1 Do", Encoding.ASCII.GetString(read.Pages[0].GetContentData()), StringComparison.Ordinal);
    }

    [Fact]
    public void OpaqueImageHasNoSoftMask()
    {
        using var image = Image.ImportPixelData<Rgba32>([new Rgba32(1, 2, 3, 255)], 1, 1);

        Assert.False(PdfImage.FromImage(image).HasSoftMask);
    }

    [Fact]
    public void SixteenBitImagesAreBigEndian()
    {
        using var image = Image.ImportPixelData<Gray16>([new Gray16(0x1234)], 1, 1);
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A5).CreateContentBuilder().DrawImage(PdfImage.FromImage(image), 0, 0, 1, 1);

        var stream = (PdfStream)document.Objects.GetReferences().Select(r => r.Resolve()).Single(o => o is PdfStream s && s.Dictionary.ContainsKey(PdfName.Create("Width")));

        Assert.Equal([0x12, 0x34], stream.GetDecodedData());
        Assert.Equal(16, stream.Dictionary.GetInteger(PdfName.Create("BitsPerComponent")));
    }

    [Fact]
    public void ImageIsSharedByThePagesOfADocument()
    {
        using var image = Image.ImportPixelData<Rgb24>([new Rgb24(1, 2, 3)], 1, 1);
        var pdfImage = PdfImage.FromImage(image);
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A5).CreateContentBuilder().DrawImage(pdfImage, 0, 0, 1, 1).DrawImage(pdfImage, 5, 5, 1, 1);
        document.Pages.Add(PdfSize.A5).CreateContentBuilder().DrawImage(pdfImage, 0, 0, 1, 1);

        var images = document.Objects.GetReferences().Select(r => r.Resolve()).OfType<PdfStream>().Count(s => s.Dictionary.GetName(PdfName.Create("Subtype"))?.Value == "Image");

        Assert.Equal(1, images);
    }

    [Fact]
    public void JpegIsKeptAsItIs()
    {
        using var image = Image.ImportPixelData<Rgb24>(Enumerable.Repeat(new Rgb24(200, 100, 50), 64).ToArray(), 8, 8);
        using var jpeg = new MemoryStream();
        image.Save(jpeg, new Imaging.Formats.JpegEncoder());

        var pdfImage = PdfImage.Load(jpeg.ToArray());
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A5).CreateContentBuilder().DrawImage(pdfImage, 0, 0, 8, 8);
        var stream = (PdfStream)document.Objects.GetReferences().Select(r => r.Resolve()).Single(o => o is PdfStream s && s.Dictionary.ContainsKey(PdfName.Create("Width")));

        Assert.Equal("DCTDecode", stream.Filters.Single().Value);
        Assert.Equal(jpeg.ToArray(), stream.EncodedData.ToArray());
        Assert.HasCount(8 * 8 * 3, stream.GetDecodedData());
    }
}
