using System.Globalization;
using System.Text;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering;

namespace Meziantou.Framework.Pdf.Tests;

public sealed class PdfNavigationTests
{
    [Theory]
    [InlineData("https://bücher.example/ü?q=é#f", "https://xn--bcher-kva.example/%C3%BC?q=%C3%A9#f")]
    [InlineData("https://例え.テスト/", "https://xn--r8jz45g.xn--zckzah/")]
    [InlineData("https://user@MÜNCHEN.example:8443/a", "https://user@xn--mnchen-3ya.example:8443/a")]
    [InlineData("https://example.com/path", "https://example.com/path")]
    [InlineData("mailto:josé@example.com", "mailto:jos%C3%A9@example.com")]
    public void LinksToInternationalizedUrisAreAscii(string uri, string expected)
    {
        var document = new PdfDocument();
        var link = document.Pages.Add(PdfSize.A5).Annotations.AddLink(new PdfRectangle(0, 0, 10, 10), new Uri(uri));

        var written = link.Dictionary.Get<PdfDictionary>(PdfNames.A)!.Get<PdfString>(PdfNames.URI)!;
        Assert.Equal(expected, Encoding.ASCII.GetString(written.Bytes.Span));

        var read = PdfDocument.Open(document.ToByteArray()).Pages[0].Annotations.OfType<PdfLinkAnnotation>().Single();
        Assert.Equal(new Uri(expected), read.Uri);
    }

    [Theory]
    [InlineData("bücher", "bcher-kva")]
    [InlineData("例え", "r8jz45g")]
    [InlineData("ليهمابتكلموشعربي؟", "egbpdaj6bu4bxfgehfvwxn")] // RFC 3492, 7.1 (A)
    [InlineData("他们为什么不说中文", "ihqwcrb4cv8a8dqg056pqjye")] // RFC 3492, 7.1 (B)
    [InlineData("3年B組金八先生", "3B-ww4c5e180e575a65lsy2b")] // RFC 3492, 7.1 (L)
    public void Punycode(string label, string expected)
    {
        Assert.Equal(expected, PdfUriEncoder.EncodePunycode(label));
        var document = new PdfDocument();
        var link = document.Pages.Add(PdfSize.A5).Annotations.AddLink(new PdfRectangle(0, 0, 10, 10), new Uri("https://" + label + ".example/"));

        var written = Encoding.ASCII.GetString(link.Dictionary.Get<PdfDictionary>(PdfNames.A)!.Get<PdfString>(PdfNames.URI)!.Bytes.Span);
        Assert.Equal("https://xn--" + PdfUriEncoder.EncodePunycode(label.ToLowerInvariant()) + ".example/", written);
    }

    [Fact]
    public void AnOutlineItemIsInOneCollectionOnce()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(PdfSize.A5);
        var x = document.Outlines.Add("x", PdfDestination.Fit(page));
        var y = document.Outlines.Add("y");

        Assert.Throws<InvalidOperationException>(() => document.Outlines.Add(x));
        Assert.Throws<InvalidOperationException>(() => document.Outlines.Insert(0, x));
        Assert.Throws<InvalidOperationException>(() => document.Outlines[1] = x);
        Assert.Throws<InvalidOperationException>(() => y.Children.Add(x));

        // An item cannot contain itself
        var child = y.Children.Add("child");
        var grandChild = child.Children.Add("grand child");
        Assert.Throws<InvalidOperationException>(() => y.Children.Add(y));
        document.Outlines.Remove(y);
        Assert.Throws<InvalidOperationException>(() => grandChild.Children.Add(y));
        Assert.Throws<InvalidOperationException>(() => child.Children[0] = y);

        // An item removed can be added elsewhere; replacing an item frees it
        document.Outlines.Remove(x);
        child.Children.Add(x);
        document.Outlines.Add(y);
        var z = new PdfOutlineItem("z");
        child.Children[1] = z;
        document.Outlines.Insert(0, x);
        document.Outlines[0] = x;

        var read = PdfDocument.Open(document.ToByteArray());
        Assert.Equal(["x", "y"], read.Outlines.Select(item => item.Title));
        Assert.Equal(["grand child", "z"], read.Outlines[1].Children[0].Children.Select(item => item.Title));
        Assert.Same(read.Pages[0], read.Outlines[0].Destination!.Page);
    }

    [Fact]
    public void TheItemsOfAnOutlineReadCanBeMoved()
    {
        var document = new PdfDocument();
        document.Outlines.Add("a").Children.Add("b");
        var read = PdfDocument.Open(document.ToByteArray());

        var b = read.Outlines[0].Children[0];
        Assert.Throws<InvalidOperationException>(() => read.Outlines.Add(b));
        read.Outlines[0].Children.Clear();
        read.Outlines.Add(b);

        Assert.Equal(["a", "b"], PdfDocument.Open(read.ToByteArray()).Outlines.Select(item => item.Title));
    }

    /// <summary>A name tree of two leaves whose keys are sorted by bytes: PDFDocEncoding keys, then UTF-16BE keys (byte 0xFE first).</summary>
    private static PdfDocument CreateNameTree()
    {
        // "z" < "é" (0xE9) < UTF-16 "à" (FE FF 00 E0) < UTF-16 "中": ordinal text order would put "à" before "é" and "z"
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /Names << /Dests 10 0 R >> >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R 4 0 R 5 0 R 6 0 R] /Count 4 /MediaBox [0 0 100 100] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R >>")
            .Add(4, "<< /Type /Page /Parent 2 0 R >>")
            .Add(5, "<< /Type /Page /Parent 2 0 R >>")
            .Add(6, "<< /Type /Page /Parent 2 0 R >>")
            .Add(10, "<< /Kids [11 0 R 12 0 R] >>")
            .Add(11, "<< /Limits [(z) <E9>] /Names [(z) [3 0 R /Fit] <E9> [4 0 R /Fit]] >>")
            .Add(12, "<< /Limits [<FEFF00E0> <FEFF4E2D>] /Names [<FEFF00E0> [5 0 R /Fit] <FEFF4E2D> [6 0 R /Fit]] >>");
        return PdfDocument.Open(file.ToArray());
    }

    [Theory]
    [InlineData("(z)", 0)]
    [InlineData("<E9>", 1)]
    [InlineData("<FEFF00E9>", 1)]
    [InlineData("<FEFF00E0>", 2)]
    [InlineData("<E0>", 2)]
    [InlineData("<FEFF4E2D>", 3)]
    [InlineData("<EFBBBFE4B8AD>", 3)]
    public void NameTreesAreSearchedInTheOrderOfTheBytes(string key, int page)
    {
        var document = CreateNameTree();
        var link = new PdfDictionary { [PdfNames.Dest] = Parse(key) };

        var read = PdfDocument.Open(Link(document, link));
        var destination = read.Pages[0].Annotations.OfType<PdfLinkAnnotation>().Single().Destination;

        Assert.Equal(page, read.Pages.IndexOf(destination!.Page));
    }

    [Fact]
    public void AnUnknownNameHasNoDestination()
    {
        var document = CreateNameTree();
        var link = new PdfDictionary { [PdfNames.Dest] = PdfString.FromText("missing") };

        Assert.Null(PdfDocument.Open(Link(document, link)).Pages[0].Annotations.OfType<PdfLinkAnnotation>().Single().Destination);
    }

    [Fact]
    public void LargeNameTreesAreSearchedWithoutDecodingEveryKey()
    {
        // One leaf of 20000 sorted keys, and lookups of every key: a linear search would read 200 million keys
        var builder = new StringBuilder();
        for (var i = 0; i < 20000; i++)
        {
            builder.Append(CultureInfo.InvariantCulture, $"(k{i:D6}) [3 0 R /XYZ 0 {i} 0] ");
        }

        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /Names << /Dests << /Names [" + builder + "] >> >> >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R >>");
        var document = PdfDocument.Open(file.ToArray());
        var annotations = new PdfArray();
        for (var i = 0; i < 20000; i++)
        {
            annotations.Add(document.Objects.Add(new PdfDictionary
            {
                [PdfNames.Subtype] = PdfNames.Link,
                [PdfNames.Rect] = PdfArray.FromNumbers(0, 0, 1, 1),
                [PdfNames.Dest] = PdfString.FromText(string.Create(CultureInfo.InvariantCulture, $"k{i:D6}")),
            }));
        }

        document.Pages[0].Dictionary[PdfNames.Annots] = annotations;
        var links = document.Pages[0].Annotations;
        for (var i = 0; i < links.Count; i++)
        {
            Assert.Equal((double)i, ((PdfLinkAnnotation)links[i]).Destination!.Top);
        }
    }

    [Fact]
    public void AStringDestinationFallsBackToTheDestsDictionary()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /Dests << /legacy [4 0 R /Fit] /dict << /D [3 0 R /FitH 5] >> >> /Names << /Dests << /Names [(modern) [3 0 R /Fit]] >> >> >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 /MediaBox [0 0 100 100] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Annots [5 0 R 6 0 R 7 0 R 8 0 R 9 0 R] >>")
            .Add(4, "<< /Type /Page /Parent 2 0 R >>")
            .Add(5, "<< /Subtype /Link /Rect [0 0 1 1] /Dest (legacy) >>")
            .Add(6, "<< /Subtype /Link /Rect [0 0 1 1] /Dest /modern >>")
            .Add(7, "<< /Subtype /Link /Rect [0 0 1 1] /A << /S /GoTo /D (dict) >> >>")
            .Add(8, "<< /Subtype /Link /Rect [0 0 1 1] /Dest (unknown) >>")
            .Add(9, "<< /Subtype /Link /Rect [0 0 1 1] /Dest () >>");
        var document = PdfDocument.Open(file.ToArray());

        var destinations = document.Pages[0].Annotations.OfType<PdfLinkAnnotation>().Select(link => link.Destination).ToList();
        Assert.Same(document.Pages[1], destinations[0]!.Page);
        Assert.Same(document.Pages[0], destinations[1]!.Page);
        Assert.Equal(PdfDestinationKind.FitHorizontally, destinations[2]!.Kind);
        Assert.Equal(5, destinations[2]!.Top);
        Assert.Null(destinations[3]);
        Assert.Null(destinations[4]);
    }

    [Fact]
    public void AStringDestinationWithTheNullCharacterIsNotFound()
    {
        // A name cannot contain the null character: the string names no entry of the /Dests dictionary
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /Dests << /ab [3 0 R /Fit] >> >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Annots [4 0 R] >>")
            .Add(4, "<< /Subtype /Link /Rect [0 0 1 1] /Dest (a\\000b) >>");
        var document = PdfDocument.Open(file.ToArray());

        Assert.Null(((PdfLinkAnnotation)document.Pages[0].Annotations.Single()).Destination);
    }

    [Theory]
    [InlineData("<< /S /URI /URI (https://example.com) >>")]
    [InlineData("<< /S /Launch /F (file.exe) >>")]
    [InlineData("<< /S /GoTo /D [3 0 R /Fit] >>")]
    public void ADestinationReplacesTheActionOfALinkOrAnItem(string action)
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /Outlines 10 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R >>")
            .Add(10, "<< /Type /Outlines /First 11 0 R /Last 11 0 R /Count 1 >>")
            .Add(11, "<< /Title (Item) /Parent 10 0 R /A " + action + " >>");
        var document = PdfDocument.Open(file.ToArray());
        document.Outlines[0].Destination = PdfDestination.Xyz(document.Pages[0], 0, 50);

        var read = PdfDocument.Open(document.ToByteArray());
        var item = read.Catalog.Get<PdfDictionary>(PdfNames.Outlines)!.Get<PdfDictionary>(PdfNames.First)!;
        Assert.False(item.ContainsKey(PdfNames.A));
        Assert.Equal(50, read.Outlines[0].Destination!.Top);
    }

    [Fact]
    public void TheAnnotationsOfAPageAreReadOnce()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(PdfSize.A4);
        for (var i = 0; i < 5000; i++)
        {
            page.Annotations.AddLink(new PdfRectangle(0, 0, 1, 1), new Uri("https://example.com/" + i.ToString(CultureInfo.InvariantCulture)));
        }

        var annotations = page.Annotations;
        var first = annotations[0];
        for (var i = 0; i < annotations.Count; i++)
        {
            Assert.Same(annotations[i], annotations[i]);
        }

        Assert.Same(first, annotations[0]);

        // A change of the collection or of the /Annots array is seen
        annotations.Remove(first);
        Assert.HasCount(4999, annotations);
        Assert.NotSame(first, annotations[0]);
        page.Dictionary.Get<PdfArray>(PdfNames.Annots)!.RemoveAt(0);
        Assert.HasCount(4998, annotations);
        page.Dictionary[PdfNames.Annots] = new PdfArray();
        Assert.Empty(annotations);

        // Removing while enumerating goes on over the annotations of the start
        page.Annotations.AddLink(new PdfRectangle(0, 0, 1, 1), new Uri("https://example.com/a"));
        page.Annotations.AddLink(new PdfRectangle(0, 0, 1, 1), new Uri("https://example.com/b"));
        foreach (var annotation in annotations)
        {
            annotations.Remove(annotation);
        }

        Assert.Empty(annotations);
    }

    [Fact]
    public void AnnotationFlags()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Annots [4 0 R 5 0 R] >>")
            .Add(4, "<< /Subtype /Text /Rect [0 0 1 1] /F 4100 >>")
            .Add(5, "<< /Subtype /Square /Rect [0 0 1 1] >>");
        var document = PdfDocument.Open(file.ToArray());
        var annotations = document.Pages[0].Annotations;

        // 4100 = Print (4) and an unknown bit (4096)
        Assert.Equal(PdfAnnotationFlags.Print | (PdfAnnotationFlags)4096, annotations[0].Flags);
        Assert.Equal(PdfAnnotationFlags.None, annotations[1].Flags);
        annotations[0].Flags |= PdfAnnotationFlags.Locked | PdfAnnotationFlags.NoView;
        annotations[0].Flags &= ~PdfAnnotationFlags.Print;
        annotations[1].Flags = PdfAnnotationFlags.Hidden;

        var read = PdfDocument.Open(document.ToByteArray()).Pages[0].Annotations;
        Assert.Equal(4096 | 128 | 32, read[0].Dictionary.GetInteger(PdfNames.F));
        Assert.Equal(PdfAnnotationFlags.Hidden, read[1].Flags);

        var link = new PdfDocument().Pages.Add(PdfSize.A5).Annotations.AddLink(new PdfRectangle(0, 0, 1, 1), new Uri("https://example.com"));
        Assert.Equal(PdfAnnotationFlags.Print, link.Flags);
        link.Flags = PdfAnnotationFlags.None;
        Assert.False(link.Dictionary.ContainsKey(PdfNames.F));
    }

    [Fact]
    public void Example_MarkupAnnotations()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(PdfSize.A4);
        page.CreateContentBuilder().DrawText("Important sentence", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 12, 72, 700);

        #region Pdf_MarkupAnnotations
        var annotations = page.Annotations;
        var note = annotations.AddNote(new PdfPoint(40, 700), "Check this paragraph");
        note.Author = "Reviewer";
        annotations.AddTextMarkup(PdfTextMarkupKind.Highlight, [new PdfRectangle(72, 697, 175, 710)]);
        annotations.AddFreeText(new PdfRectangle(300, 690, 500, 730), "Free text drawn on the page", fontSize: 10);
        annotations.AddStamp(new PdfRectangle(400, 750, 550, 800), "APPROVED", PdfColor.FromRgb(0, 0.5, 0));
        #endregion

        var read = PdfDocument.Open(document.ToByteArray()).Pages[0].Annotations.OfType<PdfMarkupAnnotation>().ToList();
        Assert.Equal(["Text", "Highlight", "FreeText", "Stamp"], read.Select(annotation => annotation.Subtype));
        Assert.Equal("Reviewer", read[0].Author);
        Assert.Equal("Check this paragraph", read[0].Contents);
        Assert.Equal(PdfColor.FromRgb(1, 1, 0), read[1].Color);
        Assert.Equal(PdfName.Create("Approved"), read[3].Dictionary.GetName(PdfNames.Name));
        Assert.All(read, annotation => Assert.NotNull(annotation.Dictionary.Get<PdfDictionary>(PdfName.Create("AP"))?.Get<PdfStream>(PdfNames.N)));
        Assert.Equal("/Helvetica 10 Tf 0 g", read[2].Dictionary.GetText(PdfName.Create("DA")));
    }

    [Theory]
    [InlineData(PdfTextMarkupKind.Highlight, 255, 255, 0)]
    [InlineData(PdfTextMarkupKind.Underline, 230, 0, 0)]
    [InlineData(PdfTextMarkupKind.StrikeOut, 230, 0, 0)]
    public void TextMarkupsAreDrawn(PdfTextMarkupKind kind, int r, int g, int b)
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(100, 100));
        var annotation = page.Annotations.AddTextMarkup(kind, [new PdfRectangle(10, 10, 90, 38), new PdfRectangle(10, 50, 50, 78)], text: "comment");

        Assert.Equal([10, 38, 90, 38, 10, 10, 90, 10, 10, 78, 50, 78, 10, 50, 50, 50], annotation.Dictionary.Get<PdfArray>(PdfName.Create("QuadPoints"))!.Select(item => ((PdfNumber)item).DoubleValue));
        Assert.Equal(new PdfRectangle(10, 10, 90, 78), annotation.Rectangle);
        using var image = PdfDocument.Open(document.ToByteArray()).Pages[0].Render(new PdfRenderOptions { UseSystemFonts = false });
        var y = kind switch { PdfTextMarkupKind.Highlight => 30, PdfTextMarkupKind.Underline => 12, _ => 21 };
        var pixel = image.Frames[0][50, 99 - y];
        Assert.True(Math.Abs(pixel.R - r) <= 3 && Math.Abs(pixel.G - g) <= 3 && Math.Abs(pixel.B - b) <= 3, $"({pixel.R}, {pixel.G}, {pixel.B})");
        Assert.Equal(255, image.Frames[0][70, 99 - 60].G);
    }

    [Fact]
    public void MarkupAnnotationProperties()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Annots [4 0 R 5 0 R] >>")
            .Add(4, "<< /Subtype /Square /Rect [0 0 1 1] /T (Ann) /Subj (Shape) /CA 0.5 /C [0 0 1] /CreationDate (D:20240102030405Z) >>")
            .Add(5, "<< /Subtype /Widget /Rect [0 0 1 1] /C [] >>");
        var annotations = PdfDocument.Open(file.ToArray()).Pages[0].Annotations;

        var square = Assert.IsType<PdfSquareAnnotation>(annotations[0]);
        Assert.Equal("Ann", square.Author);
        Assert.Equal("Shape", square.Subject);
        Assert.Equal(0.5, square.Opacity);
        Assert.Equal(PdfColor.FromRgb(0, 0, 1), square.Color);
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero), square.CreationDate);
        Assert.IsType<PdfWidgetAnnotation>(annotations[1]);
        Assert.Null(annotations[1].Color);

        square.Opacity = 1;
        square.Color = null;
        square.CreationDate = null;
        Assert.False(square.Dictionary.ContainsKey(PdfName.Create("CA")));
        Assert.False(square.Dictionary.ContainsKey(PdfNames.C));
        Assert.Throws<ArgumentOutOfRangeException>(() => square.Opacity = 2);
    }

    [Fact]
    public void AnnotationsAreReadAsTheirTypes()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 200 200] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Annots [4 0 R 5 0 R 6 0 R 7 0 R 8 0 R 9 0 R 10 0 R 11 0 R 12 0 R 13 0 R 14 0 R 15 0 R 16 0 R 17 0 R 18 0 R] >>")
            .Add(4, "<< /Subtype /Text /Rect [0 0 20 20] /Name /Key /Open true /State (Accepted) /StateModel (Review) /Popup 5 0 R /NM (n1) /M (D:20240102030405Z) >>")
            .Add(5, "<< /Subtype /Popup /Rect [20 20 120 80] /Parent 4 0 R /Open true >>")
            .Add(6, "<< /Subtype /FreeText /Rect [0 0 100 30] /DA (/Helvetica 9 Tf 0 0 1 rg) /Q 2 /CL [10 10 20 20 30 20] /LE /OpenArrow /IT /FreeTextCallout >>")
            .Add(7, "<< /Subtype /Line /Rect [0 0 100 100] /L [10 20 90 80] /LE [/Circle /ClosedArrow] /IC [1 0 0] /LL 5 /LLE 2 /Cap true >>")
            .Add(8, "<< /Subtype /Square /Rect [0 0 10 10] /IC [0 1 0] >>")
            .Add(9, "<< /Subtype /Circle /Rect [0 0 10 10] /IC [0.5] >>")
            .Add(10, "<< /Subtype /Polygon /Rect [0 0 10 10] /Vertices [0 0 10 0 5 10] >>")
            .Add(11, "<< /Subtype /PolyLine /Rect [0 0 10 10] /Vertices [0 0 10 0 5] /LE [/None /Slash] >>")
            .Add(12, "<< /Subtype /Ink /Rect [0 0 10 10] /InkList [[0 0 1 1] [2 2]] >>")
            .Add(13, "<< /Subtype /Squiggly /Rect [0 0 10 10] /QuadPoints [0 10 10 10 0 0 10 0] >>")
            .Add(14, "<< /Subtype /Stamp /Rect [0 0 10 10] /Name /Confidential >>")
            .Add(15, "<< /Subtype /Caret /Rect [0 0 10 10] /Sy /P >>")
            .Add(16, "<< /Subtype /Redact /Rect [0 0 10 10] /OverlayText (Removed) /Repeat true /IC [0 0 0] >>")
            .Add(17, "<< /Subtype /Widget /Rect [0 0 10 10] /AS /On /H /P /MK << /R 450 /BC [1 0 0] /BG [1] /CA (OK) /RC (Over) /AC (Down) >> >>")
            .Add(18, "<< /Subtype /Text /Rect [0 0 20 20] /IRT 4 0 R /RT /Group >>");
        var annotations = PdfDocument.Open(file.ToArray()).Pages[0].Annotations;

        var note = Assert.IsType<PdfTextAnnotation>(annotations[0]);
        Assert.Equal("Key", note.Icon);
        Assert.True(note.IsOpen);
        Assert.Equal("Accepted", note.State);
        Assert.Equal("Review", note.StateModel);
        Assert.Equal("n1", note.UniqueName);
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero), note.ModificationDate);
        Assert.Same(annotations[1], note.Popup);

        var popup = Assert.IsType<PdfPopupAnnotation>(annotations[1]);
        Assert.Same(note, popup.Parent);
        Assert.True(popup.IsOpen);

        var freeText = Assert.IsType<PdfFreeTextAnnotation>(annotations[2]);
        Assert.Equal("/Helvetica 9 Tf 0 0 1 rg", freeText.DefaultAppearance);
        Assert.Equal(PdfTextAlignment.Right, freeText.Alignment);
        Assert.Equal([new PdfPoint(10, 10), new PdfPoint(20, 20), new PdfPoint(30, 20)], freeText.CalloutLine);
        Assert.Equal(PdfLineEnding.OpenArrow, freeText.CalloutLineEnding);
        Assert.Equal("FreeTextCallout", freeText.Intent);

        var line = Assert.IsType<PdfLineAnnotation>(annotations[3]);
        Assert.Equal(new PdfPoint(10, 20), line.Start);
        Assert.Equal(new PdfPoint(90, 80), line.End);
        Assert.Equal(PdfLineEnding.Circle, line.StartEnding);
        Assert.Equal(PdfLineEnding.ClosedArrow, line.EndEnding);
        Assert.Equal(PdfColor.FromRgb(1, 0, 0), line.InteriorColor);
        Assert.Equal(5, line.LeaderLineLength);
        Assert.Equal(2, line.LeaderLineExtension);
        Assert.True(line.ShowCaption);

        Assert.Equal(PdfColor.FromRgb(0, 1, 0), Assert.IsType<PdfSquareAnnotation>(annotations[4]).InteriorColor);
        Assert.Equal(PdfColor.FromGray(0.5), Assert.IsType<PdfCircleAnnotation>(annotations[5]).InteriorColor);
        Assert.Equal([new PdfPoint(0, 0), new PdfPoint(10, 0), new PdfPoint(5, 10)], Assert.IsType<PdfPolygonAnnotation>(annotations[6]).Vertices);

        // An odd number of coordinates: the last one is ignored
        var polyline = Assert.IsType<PdfPolyLineAnnotation>(annotations[7]);
        Assert.Equal([new PdfPoint(0, 0), new PdfPoint(10, 0)], polyline.Vertices);
        Assert.Equal(PdfLineEnding.None, polyline.StartEnding);
        Assert.Equal(PdfLineEnding.Slash, polyline.EndEnding);

        var ink = Assert.IsType<PdfInkAnnotation>(annotations[8]);
        Assert.HasCount(2, ink.Strokes);
        Assert.Equal([new PdfPoint(0, 0), new PdfPoint(1, 1)], ink.Strokes[0]);
        Assert.Equal([new PdfPoint(2, 2)], ink.Strokes[1]);

        var markup = Assert.IsType<PdfTextMarkupAnnotation>(annotations[9]);
        Assert.Equal(PdfTextMarkupKind.Squiggly, markup.Kind);
        Assert.Equal([new PdfRectangle(0, 0, 10, 10)], markup.Regions);

        Assert.Equal("Confidential", Assert.IsType<PdfStampAnnotation>(annotations[10]).Icon);
        Assert.True(Assert.IsType<PdfCaretAnnotation>(annotations[11]).ShowsParagraphSymbol);

        var redact = Assert.IsType<PdfRedactAnnotation>(annotations[12]);
        Assert.Equal([new PdfRectangle(0, 0, 10, 10)], redact.Regions);
        Assert.Equal("Removed", redact.OverlayText);
        Assert.True(redact.RepeatOverlayText);
        Assert.Equal(PdfColor.FromRgb(0, 0, 0), redact.InteriorColor);

        var widget = Assert.IsType<PdfWidgetAnnotation>(annotations[13]);
        Assert.Equal("On", widget.AppearanceState);
        Assert.Equal(PdfWidgetHighlightMode.Push, widget.HighlightMode);
        Assert.Equal(90, widget.AppearanceCharacteristics.Rotation);
        Assert.Equal(PdfColor.FromRgb(1, 0, 0), widget.AppearanceCharacteristics.BorderColor);
        Assert.Equal(PdfColor.FromGray(1), widget.AppearanceCharacteristics.BackgroundColor);
        Assert.Equal("OK", widget.AppearanceCharacteristics.NormalCaption);
        Assert.Equal("Over", widget.AppearanceCharacteristics.RolloverCaption);
        Assert.Equal("Down", widget.AppearanceCharacteristics.DownCaption);

        var reply = Assert.IsType<PdfTextAnnotation>(annotations[14]);
        Assert.Same(note, reply.InReplyTo);
        Assert.Equal(PdfAnnotationReplyType.Group, reply.ReplyType);
        Assert.Equal([reply], note.GetReplies());
    }

    [Fact]
    public void AnnotationPropertiesAreWritten()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(200, 200));
        var line = page.Annotations.AddLine(new PdfPoint(10, 10), new PdfPoint(100, 100));
        line.Start = new PdfPoint(20, 30);
        line.EndEnding = PdfLineEnding.ReversedClosedArrow;
        line.InteriorColor = PdfColor.FromCmyk(0, 0, 0, 1);
        line.LeaderLineLength = -4;
        line.ShowCaption = true;
        line.UniqueName = "line-1";
        line.ModificationDate = new DateTimeOffset(2025, 6, 7, 8, 9, 10, TimeSpan.Zero);
        var widget = (PdfWidgetAnnotation)document.Form.AddTextField(page, "name", new PdfRectangle(10, 150, 100, 170)).Widgets[0];
        widget.HighlightMode = PdfWidgetHighlightMode.Outline;
        widget.AppearanceCharacteristics.Rotation = -90;
        widget.AppearanceCharacteristics.BackgroundColor = PdfColor.FromGray(0.9);

        var read = PdfDocument.Open(document.ToByteArray()).Pages[0].Annotations;
        var readLine = Assert.IsType<PdfLineAnnotation>(read[0]);
        Assert.Equal(new PdfPoint(20, 30), readLine.Start);
        Assert.Equal(new PdfPoint(100, 100), readLine.End);
        Assert.Equal(PdfLineEnding.None, readLine.StartEnding);
        Assert.Equal(PdfLineEnding.ReversedClosedArrow, readLine.EndEnding);
        Assert.Equal(PdfColor.FromCmyk(0, 0, 0, 1), readLine.InteriorColor);
        Assert.Equal(-4, readLine.LeaderLineLength);
        Assert.True(readLine.ShowCaption);
        Assert.Equal("line-1", readLine.UniqueName);
        Assert.Equal(new DateTimeOffset(2025, 6, 7, 8, 9, 10, TimeSpan.Zero), readLine.ModificationDate);
        var readWidget = Assert.IsType<PdfWidgetAnnotation>(read[1]);
        Assert.Equal(PdfWidgetHighlightMode.Outline, readWidget.HighlightMode);
        Assert.Equal(270, readWidget.AppearanceCharacteristics.Rotation);
        Assert.Equal(PdfColor.FromGray(0.9), readWidget.AppearanceCharacteristics.BackgroundColor);
        Assert.Throws<ArgumentOutOfRangeException>(() => readWidget.AppearanceCharacteristics.Rotation = 45);
    }

    [Fact]
    public void TheBorderIsReadFromTheBorderStyleOrTheBorderArray()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Annots [4 0 R 5 0 R 6 0 R] >>")
            .Add(4, "<< /Subtype /Square /Rect [0 0 10 10] /Border [0 0 3 [2 1]] >>")
            .Add(5, "<< /Subtype /Square /Rect [0 0 10 10] /Border [0 0 9] /BS << /W 2 /S /I >> /BE << /S /C /I 1.5 >> >>")
            .Add(6, "<< /Subtype /Square /Rect [0 0 10 10] >>");
        var annotations = PdfDocument.Open(file.ToArray()).Pages[0].Annotations;

        var array = annotations[0].Border;
        Assert.Equal(3, array.Width);
        Assert.Equal(PdfAnnotationBorderStyle.Dashed, array.BorderStyle);
        Assert.Equal([2, 1], array.DashPattern);

        var style = annotations[1].Border;
        Assert.Equal(2, style.Width);
        Assert.Equal(PdfAnnotationBorderStyle.Inset, style.BorderStyle);
        Assert.Equal(1.5, style.CloudyIntensity);

        var none = annotations[2].Border;
        Assert.Equal(1, none.Width);
        Assert.Equal(PdfAnnotationBorderStyle.Solid, none.BorderStyle);
        Assert.Equal([3], none.DashPattern);
        Assert.Equal(0, none.CloudyIntensity);

        // Setting a value replaces the border array with a style dictionary that keeps its width and dashes
        array.BorderStyle = PdfAnnotationBorderStyle.Underline;
        Assert.Equal(3, array.Width);
        Assert.Equal(PdfAnnotationBorderStyle.Underline, array.BorderStyle);
        Assert.Equal([2, 1], array.DashPattern);

        none.Width = 0;
        none.CloudyIntensity = 2;
        none.DashPattern = [4, 2];
        Assert.Equal(0, none.Width);
        Assert.Equal(2, none.CloudyIntensity);
        Assert.Equal([4, 2], none.DashPattern);
        none.CloudyIntensity = 0;
        Assert.False(annotations[2].Dictionary.ContainsKey(PdfName.Create("BE")));
        Assert.Throws<ArgumentOutOfRangeException>(() => none.Width = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => none.CloudyIntensity = 3);
        Assert.Throws<ArgumentException>(() => none.DashPattern = [0, 0]);
    }

    [Fact]
    public void Example_ShapeAnnotations()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(PdfSize.A4);

        #region Pdf_ShapeAnnotations
        var annotations = page.Annotations;
        var arrow = annotations.AddLine(new PdfPoint(100, 600), new PdfPoint(250, 680), endEnding: PdfLineEnding.ClosedArrow);
        arrow.Contents = "Look here";
        annotations.AddSquare(new PdfRectangle(260, 660, 400, 720), PdfColor.FromRgb(0, 0, 0.8), interiorColor: PdfColor.FromRgb(0.9, 0.9, 1));
        annotations.AddCircle(new PdfRectangle(100, 450, 200, 520), width: 2);
        annotations.AddPolygon([new PdfPoint(300, 450), new PdfPoint(400, 450), new PdfPoint(350, 530)]);
        annotations.AddInk([[new PdfPoint(100, 300), new PdfPoint(150, 350), new PdfPoint(200, 300)]], PdfColor.FromRgb(0, 0.6, 0), width: 3);

        // A reply in the thread of the arrow, and the window that shows its text
        annotations.AddReply(arrow, "Fixed in the next version", author: "Author");
        annotations.AddPopup(arrow, new PdfRectangle(260, 560, 460, 640));
        #endregion

        var read = PdfDocument.Open(document.ToByteArray()).Pages[0].Annotations;
        Assert.Equal(["Line", "Square", "Circle", "Polygon", "Ink", "Text", "Popup"], read.Select(annotation => annotation.Subtype));
        Assert.All(read.Take(6), annotation => Assert.True(annotation.HasAppearance));
        var readArrow = Assert.IsType<PdfLineAnnotation>(read[0]);
        Assert.Equal(PdfLineEnding.ClosedArrow, readArrow.EndEnding);
        Assert.Same(read[6], readArrow.Popup);
        Assert.Equal("Fixed in the next version", Assert.Single(readArrow.GetReplies()).Contents);
        Assert.Equal("Author", ((PdfMarkupAnnotation)read[5]).Author);
    }

    [Fact]
    public void ShapeAnnotationsAreDrawn()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(100, 100));
        page.Annotations.AddSquare(new PdfRectangle(10, 10, 40, 40), PdfColor.FromRgb(0, 0, 1), PdfColor.FromRgb(1, 0, 0), width: 2);
        page.Annotations.AddInk([[new PdfPoint(60, 20), new PdfPoint(90, 20)]], PdfColor.FromRgb(0, 1, 0), width: 4);
        page.Annotations.AddLine(new PdfPoint(60, 80), new PdfPoint(90, 80), PdfColor.FromRgb(0, 0, 1), width: 2);

        using var image = PdfDocument.Open(document.ToByteArray()).Pages[0].Render(new PdfRenderOptions { UseSystemFonts = false });
        AssertPixel(image, 25, 25, 255, 0, 0);
        AssertPixel(image, 11, 25, 0, 0, 255);
        AssertPixel(image, 75, 20, 0, 255, 0);
        AssertPixel(image, 75, 80, 0, 0, 255);
        AssertPixel(image, 50, 50, 255, 255, 255);
    }

    [Theory]
    [InlineData("<< /Subtype /Text /Rect [10 10 30 30] /C [0 0 1] >>", 15, 22, 0, 0, 255)]
    [InlineData("<< /Subtype /FreeText /Rect [5 5 95 95] /Contents (IIIIIIII IIIIIIII IIIIIIII) /DA (/Helvetica-Bold 30 Tf 1 0 0 rg) /BS << /W 3 >> >>", 6, 50, 255, 0, 0)]
    [InlineData("<< /Subtype /Stamp /Rect [5 30 95 70] /Name /Draft /C [0 0.5 0] >>", 7, 50, 0, 128, 0)]
    [InlineData("<< /Subtype /Caret /Rect [20 10 80 90] /C [0 0 1] >>", 50, 50, 0, 0, 255)]
    [InlineData("<< /Subtype /Redact /Rect [10 10 90 90] >>", 10, 50, 255, 0, 0)]
    public void UpdateAppearanceDrawsTheAnnotationsTheRendererLeavesToTheViewers(string annotation, int x, int y, int r, int g, int b)
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Annots [4 0 R] >>")
            .Add(4, annotation);
        var document = PdfDocument.Open(file.ToArray());
        var item = document.Pages[0].Annotations[0];
        Assert.False(item.HasAppearance);

        Assert.True(item.UpdateAppearance());

        Assert.True(item.HasAppearance);
        using var image = PdfDocument.Open(document.ToByteArray()).Pages[0].Render(new PdfRenderOptions { UseSystemFonts = false });
        AssertPixel(image, x, y, r, g, b, tolerance: 40);
    }

    [Fact]
    public void SettingTheRegionsOfAMarkupMovesItsRectangle()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(200, 200));
        var highlight = (PdfTextMarkupAnnotation)page.Annotations.AddTextMarkup(PdfTextMarkupKind.Highlight, [new PdfRectangle(10, 10, 50, 20)]);
        var redaction = page.Annotations.AddRedaction([new PdfRectangle(10, 10, 50, 20)]);

        highlight.Regions = [new PdfRectangle(100, 150, 180, 160), new PdfRectangle(100, 135, 140, 145)];
        redaction.Regions = [new PdfRectangle(120, 100, 160, 110)];

        Assert.Equal(new PdfRectangle(100, 135, 180, 160), highlight.Rectangle);
        Assert.Equal(new PdfRectangle(120, 100, 160, 110), redaction.Rectangle);
        Assert.True(highlight.UpdateAppearance());
        var box = highlight.Dictionary.Get<PdfDictionary>(PdfName.Create("AP"))!.Get<PdfStream>(PdfNames.N)!.Dictionary.Get<PdfArray>(PdfName.Create("BBox"))!;
        Assert.True(PdfRectangle.TryRead(box, out var bounds));
        Assert.True(bounds.Width >= 80 && bounds.Height >= 25);
    }

    [Fact]
    public void UpdateAppearanceDrawsTheTextAfterManySpaces()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Annots [4 0 R] >>")
            .Add(4, "<< /Subtype /FreeText /Rect [0 0 100 20] /Contents (" + new string(' ', 150) + "Visible) /DA (/Helvetica 12 Tf 0 g) /BS << /W 0 >> >>");
        var document = PdfDocument.Open(file.ToArray());
        var item = document.Pages[0].Annotations[0];

        Assert.True(item.UpdateAppearance());

        var appearance = item.Dictionary.Get<PdfDictionary>(PdfName.Create("AP"))!.Get<PdfStream>(PdfNames.N)!;
        Assert.Contains("(Visible) Tj", Encoding.Latin1.GetString(appearance.GetDecodedData()), StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateAppearanceReturnsFalseForTheTypesItDoesNotDraw()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(100, 100));
        var link = page.Annotations.AddLink(new PdfRectangle(0, 0, 10, 10), new Uri("https://example.com"));
        var square = page.Annotations.AddSquare(new PdfRectangle(0, 0, 10, 10));
        square.Color = null;

        Assert.False(link.UpdateAppearance());
        Assert.False(square.UpdateAppearance());
    }

    [Fact]
    public void RemovingAnAnnotationRemovesItsPopupAndTheReferencesToIt()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(200, 200));
        var note = page.Annotations.AddNote(new PdfPoint(10, 10), "note");
        page.Annotations.AddPopup(note, new PdfRectangle(50, 50, 150, 100));
        var reply = page.Annotations.AddReply(note, "reply");
        var element = document.StructureTree.AddElement("Annot");
        element.AddAnnotation(note);

        Assert.True(page.Annotations.Remove(note));

        Assert.Same(reply.Dictionary, Assert.Single(page.Annotations).Dictionary);
        Assert.Null(reply.InReplyTo);
        Assert.True(element.Dictionary.GetResolved(PdfNames.K) is null or PdfArray { Count: 0 });
        Assert.False(page.Annotations.Remove(note));
    }

    [Fact]
    public void AnnotationsCannotReferToAnnotationsOfAnotherDocumentOrPage()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(200, 200));
        var other = document.Pages.Add(new PdfSize(200, 200));
        var note = page.Annotations.AddNote(new PdfPoint(10, 10), "note");
        var foreign = new PdfDocument().Pages.Add(new PdfSize(10, 10)).Annotations.AddNote(new PdfPoint(0, 0), "x");

        Assert.Throws<ArgumentException>(() => other.Annotations.AddPopup(note, new PdfRectangle(0, 0, 10, 10)));
        Assert.Throws<ArgumentException>(() => other.Annotations.AddReply(note, "reply"));
        Assert.Throws<ArgumentException>(() => note.InReplyTo = foreign);
        Assert.Throws<ArgumentException>(() => page.Annotations.AddPolygon([new PdfPoint(0, 0)]));
        Assert.Throws<ArgumentException>(() => page.Annotations.AddInk([]));
        Assert.Throws<ArgumentException>(() => page.Annotations.AddLine(new PdfPoint(double.NaN, 0), new PdfPoint(1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => page.Annotations.AddSquare(new PdfRectangle(0, 0, 1, 1), width: -1));
        Assert.Throws<ArgumentException>(() => page.Annotations.AddRedaction([]));
    }

    [Fact]
    public void Example_FlattenAnnotations()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(100, 100));
        page.Annotations.AddSquare(new PdfRectangle(10, 10, 40, 40), interiorColor: PdfColor.FromRgb(0, 0, 1));
        page.Annotations.AddLink(new PdfRectangle(60, 60, 90, 90), new Uri("https://example.com"));
        using var before = PdfDocument.Open(document.ToByteArray()).Pages[0].Render(new PdfRenderOptions { UseSystemFonts = false });

        #region Pdf_FlattenAnnotations
        // The markup annotations become content of the page; the links are kept
        var flattened = document.FlattenAnnotations();
        #endregion

        Assert.Equal(1, flattened);
        var read = PdfDocument.Open(document.ToByteArray());
        Assert.IsType<PdfLinkAnnotation>(Assert.Single(read.Pages[0].Annotations));
        using var after = read.Pages[0].Render(new PdfRenderOptions { UseSystemFonts = false });
        AssertPixel(after, 25, 25, 0, 0, 255);
        AssertPixel(after, 11, 25, before.Frames[0][11, 99 - 25].R, before.Frames[0][11, 99 - 25].G, before.Frames[0][11, 99 - 25].B, tolerance: 2);
    }

    [Fact]
    public void FlatteningAppliesTheFlagsTheOpacityAndTheFilter()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(100, 100));
        var hidden = page.Annotations.AddSquare(new PdfRectangle(0, 0, 50, 50), interiorColor: PdfColor.FromRgb(1, 0, 0), width: 0);
        hidden.Flags = PdfAnnotationFlags.NoView | PdfAnnotationFlags.Print;
        var transparent = page.Annotations.AddSquare(new PdfRectangle(50, 50, 100, 100), interiorColor: PdfColor.FromRgb(0, 0, 0), width: 0);
        transparent.Opacity = 0.5;
        var kept = page.Annotations.AddCircle(new PdfRectangle(0, 50, 50, 100), interiorColor: PdfColor.FromRgb(0, 1, 0));

        var count = page.FlattenAnnotations(new PdfAnnotationFlattenOptions { Filter = annotation => annotation is PdfSquareAnnotation });

        Assert.Equal(2, count);
        Assert.Same(kept.Dictionary, Assert.Single(page.Annotations).Dictionary);
        using var image = PdfDocument.Open(document.ToByteArray()).Pages[0].Render(new PdfRenderOptions { UseSystemFonts = false, RenderAnnotations = false });
        AssertPixel(image, 25, 25, 255, 255, 255);
        AssertPixel(image, 75, 75, 128, 128, 128, tolerance: 3);
    }

    [Fact]
    public void FlatteningForPrintingDrawsThePrintedAnnotations()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(100, 100));
        var printed = page.Annotations.AddSquare(new PdfRectangle(0, 0, 50, 50), interiorColor: PdfColor.FromRgb(1, 0, 0), width: 0);
        printed.Flags = PdfAnnotationFlags.NoView | PdfAnnotationFlags.Print;

        page.FlattenAnnotations(new PdfAnnotationFlattenOptions { Intent = PdfRenderIntent.Print });

        Assert.Empty(page.Annotations);
        using var image = PdfDocument.Open(document.ToByteArray()).Pages[0].Render(new PdfRenderOptions { UseSystemFonts = false });
        AssertPixel(image, 25, 25, 255, 0, 0);
    }

    [Fact]
    public void FlatteningKeepsTheWidgetsOfSignedSignatures()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] >> >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Annots [4 0 R 5 0 R] >>")
            .Add(4, "<< /Type /Annot /Subtype /Widget /FT /Sig /T (sig) /V << /Type /Sig >> /Rect [0 0 10 10] /P 3 0 R >>")
            .Add(5, "<< /Type /Annot /Subtype /Square /Rect [0 0 10 10] /C [1 0 0] >>");
        var document = PdfDocument.Open(file.ToArray());

        var count = document.FlattenAnnotations(new PdfAnnotationFlattenOptions { Filter = _ => true });

        Assert.Equal(1, count);
        Assert.IsType<PdfWidgetAnnotation>(Assert.Single(document.Pages[0].Annotations));
    }

    [Fact]
    public void Example_ApplyRedactions()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(200, 100));
        var font = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);
        page.CreateContentBuilder().DrawText("Public", font, 12, 10, 50).DrawText("Secret", font, 12, 110, 50);

        #region Pdf_RedactionAnnotations
        // A reviewer marks the regions to remove; the content stays until the redactions are applied
        page.Annotations.AddRedaction([new PdfRectangle(105, 45, 160, 65)], overlayText: "REDACTED");
        var applied = document.ApplyRedactions();
        #endregion

        Assert.Equal(1, applied);
        var bytes = document.ToByteArray(new PdfSaveOptions { CompressStreams = false });
        var read = PdfDocument.Open(bytes);
        Assert.Empty(read.Pages[0].Annotations);
        Assert.Equal("Public REDACTED", read.Pages[0].ExtractText().Trim());
        using var image = read.Pages[0].Render(new PdfRenderOptions { UseSystemFonts = false });
        AssertPixel(image, 106, 46, 0, 0, 0);
    }

    [Fact]
    public void ApplyingRedactionsUsesTheInteriorColorOrTheFillColor()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(100, 100));
        var redact = page.Annotations.AddRedaction([new PdfRectangle(0, 0, 40, 40)], interiorColor: PdfColor.FromRgb(0, 0, 1));
        var other = page.Annotations.AddRedaction([new PdfRectangle(60, 60, 100, 100)]);
        other.InteriorColor = null;

        Assert.Equal(2, document.ApplyRedactions(new PdfRedactionOptions { FillColor = PdfColor.FromRgb(0, 1, 0) }));

        using var image = PdfDocument.Open(document.ToByteArray()).Pages[0].Render(new PdfRenderOptions { UseSystemFonts = false });
        AssertPixel(image, 20, 20, 0, 0, 255);
        AssertPixel(image, 80, 80, 0, 255, 0);
        Assert.NotNull(redact);
    }

    [Fact]
    public void ApplyingRedactionsKeepsTheMarkersWhenTheRedactionFails()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Contents 4 0 R /Annots [5 0 R] >>")
            .Add(4, "<< /Length 4 /Filter /Unknown >>\nstream\nxxxx\nendstream")
            .Add(5, "<< /Type /Annot /Subtype /Redact /Rect [0 0 50 50] >>");
        var document = PdfDocument.Open(file.ToArray());

        Assert.Throws<UnsupportedPdfFeatureException>(() => document.ApplyRedactions());

        Assert.IsType<PdfRedactAnnotation>(Assert.Single(document.Pages[0].Annotations));
    }

    [Fact]
    public void AllTheRegionsOfARedactionAreRead()
    {
        var page = new PdfDocument().Pages.Add(new PdfSize(100, 100));
        var regions = Enumerable.Range(0, 20_000).Select(i => PdfRectangle.FromSize(i % 100, i / 1000, 1, 1)).ToList();

        var redaction = page.Annotations.AddRedaction(regions);

        Assert.HasCount(20_000, redaction.Regions);
    }

    [Fact]
    public void UpdateAppearanceOfAWidgetUsesItsField()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(200, 200));
        var text = document.Form.AddTextField(page, "name", new PdfRectangle(10, 10, 100, 30));
        text.Value = "Value";
        var checkBox = document.Form.AddCheckBox(page, "agree", new PdfRectangle(10, 50, 30, 70));
        var checkBoxWidget = (PdfWidgetAnnotation)checkBox.Widgets[0];
        var states = checkBoxWidget.Dictionary.Get<PdfDictionary>(PdfName.Create("AP"))!.Get<PdfDictionary>(PdfNames.N)!.Count;

        Assert.True(text.Widgets[0].UpdateAppearance());
        Assert.False(checkBoxWidget.UpdateAppearance());

        Assert.Equal(states, checkBoxWidget.Dictionary.Get<PdfDictionary>(PdfName.Create("AP"))!.Get<PdfDictionary>(PdfNames.N)!.Count);
        Assert.True(states >= 2);
    }

    [Fact]
    public void UpdateAppearanceComputesTheRectangleOfTheGeometry()
    {
        var page = new PdfDocument().Pages.Add(new PdfSize(200, 200));
        var line = page.Annotations.AddLine(new PdfPoint(10, 10), new PdfPoint(20, 20));
        var ink = page.Annotations.AddInk([[new PdfPoint(10, 10)]]);

        line.End = new PdfPoint(150, 160);
        ink.Strokes = [[new PdfPoint(10, 10), new PdfPoint(120, 130)]];
        line.UpdateAppearance();
        ink.UpdateAppearance();

        Assert.True(line.Rectangle.Right > 150 && line.Rectangle.Top > 160, line.Rectangle.ToString());
        Assert.True(ink.Rectangle.Right > 120 && ink.Rectangle.Top > 130, ink.Rectangle.ToString());
    }

    [Fact]
    public void FlatteningKeepsTheWidgetsOfSignaturesThatInheritTheirType()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [6 0 R] >> >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Annots [4 0 R] >>")
            .Add(4, "<< /Type /Annot /Subtype /Widget /Parent 6 0 R /T (child) /V << /Type /Sig >> /Rect [0 0 10 10] /P 3 0 R >>")
            .Add(6, "<< /FT /Sig /T (parent) /Kids [4 0 R] >>");
        var document = PdfDocument.Open(file.ToArray());

        Assert.Equal(0, document.FlattenAnnotations(new PdfAnnotationFlattenOptions { Filter = _ => true }));
        Assert.IsType<PdfWidgetAnnotation>(Assert.Single(document.Pages[0].Annotations));
    }

    [Fact]
    public void FlatteningAppliesTheOpacityToTheWholeAppearance()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(100, 100));
        var square = page.Annotations.AddSquare(new PdfRectangle(10, 10, 90, 90), PdfColor.FromRgb(0, 0, 1), PdfColor.FromRgb(1, 0, 0), width: 8);
        square.Opacity = 0.5;

        page.FlattenAnnotations();

        // The inner half of the border is over the fill: in a group, the border hides it before the opacity applies
        using var image = PdfDocument.Open(document.ToByteArray()).Pages[0].Render(new PdfRenderOptions { UseSystemFonts = false });
        AssertPixel(image, 16, 50, 128, 128, 255, tolerance: 6);
        AssertPixel(image, 50, 50, 255, 128, 128, tolerance: 6);
    }

    [Fact]
    public void TheAppearanceOfALongFreeTextIsBounded()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Annots [4 0 R] >>")
            .Add(4, "<< /Subtype /FreeText /Rect [0 0 20 20] /DA (/Helvetica 12 Tf 0 g) /Contents (" + new string('W', 1_000_000) + ") >>");
        var annotation = PdfDocument.Open(file.ToArray()).Pages[0].Annotations[0];
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        Assert.True(annotation.UpdateAppearance());

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), stopwatch.Elapsed.ToString());
    }

    [Theory]
    [InlineData("NotApproved", "NOT APPROVED")]
    [InlineData("ForPublicRelease", "FOR PUBLIC RELEASE")]
    [InlineData("Draft", "DRAFT")]
    public void TheNameOfAStampIsSplitIntoWords(string name, string expected)
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 300 100] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Annots [4 0 R] >>")
            .Add(4, "<< /Subtype /Stamp /Rect [10 10 290 90] /Name /" + name + " >>");
        var document = PdfDocument.Open(file.ToArray());

        document.FlattenAnnotations();

        Assert.Equal(expected, PdfDocument.Open(document.ToByteArray()).Pages[0].ExtractText().Trim());
    }

    [Fact]
    public void ActionsAreReadAsTheirTypes()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /OpenAction [3 0 R /Fit] /AA << /WC << /S /JavaScript /JS (close) >> >> /OCProperties << /OCGs [5 0 R] /D << >> >> >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 200 200] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Annots [4 0 R 6 0 R 7 0 R 8 0 R 9 0 R 10 0 R 11 0 R 12 0 R 13 0 R 14 0 R] /AA << /O << /S /Named /N /LastPage >> >> >>")
            .Add(4, "<< /Subtype /Link /Rect [0 0 10 10] /A << /S /GoToR /F (other.pdf) /D [2 /Fit] /NewWindow true /Next [<< /S /Named /N /NextPage >> << /S /Thread /D 0 >>] >> >>")
            .Add(5, "<< /Type /OCG /Name (Layer) >>")
            .Add(6, "<< /Subtype /Link /Rect [0 0 10 10] /A << /S /Launch /F << /Type /Filespec /UF (app.exe) >> >> >>")
            .Add(7, "<< /Subtype /Link /Rect [0 0 10 10] /A << /S /URI /URI (https://example.com/a) /IsMap true >> >>")
            .Add(8, "<< /Subtype /Link /Rect [0 0 10 10] /A << /S /SubmitForm /F << /FS /URL /F (https://example.com/submit) >> /Fields [(a) 15 0 R] /Flags 36 >> >>")
            .Add(9, "<< /Subtype /Link /Rect [0 0 10 10] /A << /S /ResetForm /Fields [(a)] /Flags 1 >> >>")
            .Add(10, "<< /Subtype /Link /Rect [0 0 10 10] /A << /S /ImportData /F (data.fdf) >> >>")
            .Add(11, "<< /Subtype /Link /Rect [0 0 10 10] /A << /S /Hide /T [(a) (b)] /H false >> >>")
            .Add(12, "<< /Subtype /Link /Rect [0 0 10 10] /A << /S /SetOCGState /State [/OFF 5 0 R /Toggle 5 0 R] /PreserveRB false >> >>")
            .Add(13, "<< /Subtype /Link /Rect [0 0 10 10] /A << /S /GoToE /T << /R /C /N (embedded.pdf) >> >> >>")
            .Add(14, "<< /Subtype /Widget /Rect [0 0 10 10] /A << /S /JavaScript /JS (app.alert(1)) >> /AA << /E << /S /Named /N /FirstPage >> /K << /S /JavaScript /JS (k) >> >> >>")
            .Add(15, "<< /T (child) /Parent 16 0 R >>")
            .Add(16, "<< /T (parent) >>");
        var document = PdfDocument.Open(file.ToArray());
        var annotations = document.Pages[0].Annotations;

        var openAction = Assert.IsType<PdfGoToAction>(document.OpenAction);
        Assert.Same(document.Pages[0], openAction.Destination!.Page);
        Assert.Equal("close", Assert.IsType<PdfJavaScriptAction>(document.AdditionalActions[PdfActionTrigger.DocumentClose]).Script);
        Assert.Equal([PdfActionTrigger.DocumentClose], document.AdditionalActions.Triggers);
        Assert.Equal("LastPage", Assert.IsType<PdfNamedAction>(document.Pages[0].AdditionalActions[PdfActionTrigger.PageOpen]).Name);

        var remote = Assert.IsType<PdfGoToRemoteAction>(((PdfLinkAnnotation)annotations[0]).Action);
        Assert.Equal("other.pdf", remote.FileName);
        Assert.Equal(2, remote.PageIndex);
        Assert.True(remote.NewWindow);
        Assert.Equal(["Named", "Thread"], remote.Next.Select(action => action.Kind));
        Assert.IsType<PdfUnknownAction>(remote.Next[1]);

        Assert.Equal("app.exe", Assert.IsType<PdfLaunchAction>(((PdfLinkAnnotation)annotations[1]).Action).FileName);
        var uri = Assert.IsType<PdfUriAction>(((PdfLinkAnnotation)annotations[2]).Action);
        Assert.Equal(new Uri("https://example.com/a"), uri.Uri);
        Assert.True(uri.IsMap);

        var submit = Assert.IsType<PdfSubmitFormAction>(((PdfLinkAnnotation)annotations[3]).Action);
        Assert.Equal(new Uri("https://example.com/submit"), submit.Url);
        Assert.Equal(["a", "parent.child"], submit.FieldNames);
        Assert.Equal(PdfSubmitFormFlags.ExportFormat | PdfSubmitFormFlags.Xfdf, submit.Flags);

        var reset = Assert.IsType<PdfResetFormAction>(((PdfLinkAnnotation)annotations[4]).Action);
        Assert.Equal(["a"], reset.FieldNames);
        Assert.True(reset.Exclude);
        Assert.Equal("data.fdf", Assert.IsType<PdfImportDataAction>(((PdfLinkAnnotation)annotations[5]).Action).FileName);
        var hide = Assert.IsType<PdfHideAction>(((PdfLinkAnnotation)annotations[6]).Action);
        Assert.Equal(["a", "b"], hide.FieldNames);
        Assert.False(hide.Hides);

        var state = Assert.IsType<PdfSetOptionalContentStateAction>(((PdfLinkAnnotation)annotations[7]).Action);
        Assert.Equal([PdfOptionalContentStateChange.Off, PdfOptionalContentStateChange.Toggle], state.Changes.Select(change => change.State));
        Assert.Equal("Layer", state.Changes[0].Group.Name);
        Assert.False(state.PreserveRadioButtons);
        Assert.Equal("embedded.pdf", Assert.IsType<PdfGoToEmbeddedAction>(((PdfLinkAnnotation)annotations[8]).Action).TargetFileName);

        var widget = Assert.IsType<PdfWidgetAnnotation>(annotations[9]);
        Assert.Equal("app.alert(1)", Assert.IsType<PdfJavaScriptAction>(widget.Action).Script);
        Assert.Equal([PdfActionTrigger.MouseEnter, PdfActionTrigger.Keystroke], widget.AdditionalActions.Triggers);
    }

    [Fact]
    public void Example_Actions()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(PdfSize.A4);
        var last = document.Pages.Add(PdfSize.A4);

        #region Pdf_Actions
        // Open the document on its last page, and run a script before it is printed
        document.OpenAction = PdfAction.GoTo(PdfDestination.Fit(last));
        document.AdditionalActions[PdfActionTrigger.DocumentPrint] = PdfAction.JavaScript("app.alert('Printing')");

        // A link that goes to the next page, then opens a website
        var link = page.Annotations.AddLink(new PdfRectangle(72, 700, 200, 720), new Uri("https://example.com"));
        var next = PdfAction.Named("NextPage");
        next.SetNext(PdfAction.OpenUri(new Uri("https://www.meziantou.net")));
        link.Action = next;

        // A bookmark that resets the form
        document.Outlines.Add("Reset").Action = PdfAction.ResetForm();
        #endregion

        var read = PdfDocument.Open(document.ToByteArray());
        Assert.Same(read.Pages[1], Assert.IsType<PdfGoToAction>(read.OpenAction).Destination!.Page);
        Assert.Equal("app.alert('Printing')", Assert.IsType<PdfJavaScriptAction>(read.AdditionalActions[PdfActionTrigger.DocumentPrint]).Script);
        var readLink = Assert.IsType<PdfLinkAnnotation>(Assert.Single(read.Pages[0].Annotations));
        var named = Assert.IsType<PdfNamedAction>(readLink.Action);
        Assert.Equal("NextPage", named.Name);
        Assert.Equal(new Uri("https://www.meziantou.net/"), Assert.IsType<PdfUriAction>(Assert.Single(named.Next)).Uri);
        Assert.Null(readLink.Uri);
        var outline = Assert.Single(read.Outlines);
        Assert.IsType<PdfResetFormAction>(outline.Action);
        Assert.Null(outline.Destination);
    }

    [Fact]
    public void ActionsAreCheckedAgainstTheirDocument()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(PdfSize.A4);
        var other = new PdfDocument();
        var otherPage = other.Pages.Add(PdfSize.A4);
        var link = page.Annotations.AddLink(new PdfRectangle(0, 0, 10, 10), new Uri("https://example.com"));

        Assert.Throws<ArgumentException>(() => link.Action = PdfAction.GoTo(PdfDestination.Fit(otherPage)));
        Assert.Throws<ArgumentException>(() => document.OpenAction = PdfAction.SetOptionalContentState(PdfOptionalContentStateChange.On, other.OptionalContent.AddGroup("x")));
        Assert.Throws<ArgumentException>(() => page.AdditionalActions[PdfActionTrigger.DocumentClose] = PdfAction.Named("FirstPage"));
        Assert.Throws<ArgumentException>(() => PdfAction.Hide([]));
        var action = PdfAction.Named("NextPage");
        Assert.Throws<ArgumentException>(() => action.SetNext(action));

        // A destination of a link is read as a go-to action, and replaced by an action
        var goTo = page.Annotations.AddLink(new PdfRectangle(0, 0, 10, 10), PdfDestination.Fit(page));
        Assert.Same(page, Assert.IsType<PdfGoToAction>(goTo.Action).Destination!.Page);
        goTo.Action = PdfAction.Named("FirstPage");
        Assert.False(goTo.Dictionary.ContainsKey(PdfNames.Dest));
        page.AdditionalActions[PdfActionTrigger.PageOpen] = PdfAction.Named("FirstPage");
        page.AdditionalActions[PdfActionTrigger.PageOpen] = null;
        Assert.False(page.Dictionary.ContainsKey(PdfName.Create("AA")));
    }

    [Fact]
    public void Example_Layers()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(100, 100));

        #region Pdf_Layers
        var notes = document.OptionalContent.AddGroup("Notes");
        var draft = document.OptionalContent.AddGroup("Draft watermark", visible: false);
        page.CreateContentBuilder()
            .BeginOptionalContent(notes).SetFillColor(PdfColor.FromRgb(0, 0, 1)).Rectangle(0, 0, 50, 50).Fill().EndMarkedContent()
            .BeginOptionalContent(draft).SetFillColor(PdfColor.FromRgb(1, 0, 0)).Rectangle(50, 50, 50, 50).Fill().EndMarkedContent();

        // The user cannot hide the notes, and the viewer shows the list of layers
        document.OptionalContent.DefaultConfiguration!.SetLocked(notes, locked: true);
        document.PageMode = PdfPageMode.UseOptionalContent;
        #endregion

        page.Annotations.AddSquare(new PdfRectangle(0, 50, 50, 100), interiorColor: PdfColor.FromRgb(0, 1, 0)).OptionalContent = draft;
        var read = PdfDocument.Open(document.ToByteArray());
        Assert.Equal(["Notes", "Draft watermark"], read.OptionalContent.Groups.Select(group => group.Name));
        Assert.True(read.OptionalContent.Groups[0].IsVisible);
        Assert.False(read.OptionalContent.Groups[1].IsVisible);
        Assert.Equal(["Notes"], read.OptionalContent.DefaultConfiguration!.LockedGroups.Select(group => group.Name));
        Assert.Equal(read.OptionalContent.Groups[1], read.Pages[0].Annotations[0].OptionalContent);
        Assert.Equal(PdfPageMode.UseOptionalContent, read.PageMode);
        Assert.Equal(["View"], read.OptionalContent.Groups[0].Intents);

        using var image = read.Pages[0].Render(new PdfRenderOptions { UseSystemFonts = false });
        AssertPixel(image, 25, 25, 0, 0, 255);
        AssertPixel(image, 75, 75, 255, 255, 255);
        AssertPixel(image, 25, 75, 255, 255, 255);

        read.OptionalContent.Groups[1].IsVisible = true;
        read.OptionalContent.DefaultConfiguration!.AddRadioButtonGroup(read.OptionalContent.Groups);
        Assert.HasCount(2, Assert.Single(read.OptionalContent.DefaultConfiguration.RadioButtonGroups));
        using var visible = PdfDocument.Open(read.ToByteArray()).Pages[0].Render(new PdfRenderOptions { UseSystemFonts = false });
        AssertPixel(visible, 75, 75, 255, 0, 0);
    }

    [Fact]
    public void OptionalContentConfigurationsAreRead()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /OCProperties << /OCGs [4 0 R 5 0 R] /D << /BaseState /OFF /ON [4 0 R] >> /Configs [<< /Name (Print) /Creator (App) /BaseState /Unchanged /OFF [4 0 R] >>] >> >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R >>")
            .Add(4, "<< /Type /OCG /Name (A) /Intent [/View /Design] >>")
            .Add(5, "<< /Type /OCG /Name (B) >>");
        var content = PdfDocument.Open(file.ToArray()).OptionalContent;

        Assert.True(content.Groups[0].IsVisible);
        Assert.False(content.Groups[1].IsVisible);
        Assert.Equal(["View", "Design"], content.Groups[0].Intents);
        Assert.Equal(PdfOptionalContentBaseState.Off, content.DefaultConfiguration!.BaseState);
        var print = Assert.Single(content.Configurations);
        Assert.Equal("Print", print.Name);
        Assert.Equal("App", print.Creator);
        Assert.Equal(PdfOptionalContentBaseState.Unchanged, print.BaseState);
        Assert.False(print.IsVisible(content.Groups[0]));
        Assert.True(print.IsVisible(content.Groups[1]));

        content.Groups[1].IsVisible = true;
        content.Groups[0].IsVisible = false;
        Assert.True(content.Groups[1].IsVisible);
        Assert.False(content.Groups[0].IsVisible);
    }

    [Fact]
    public void Example_ViewerPreferences()
    {
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4);

        #region Pdf_ViewerPreferences
        document.PageLayout = PdfPageLayout.TwoPageRight;
        document.PageMode = PdfPageMode.UseOutlines;
        var preferences = document.ViewerPreferences;
        preferences.DisplayDocumentTitle = true;
        preferences.HideToolbar = true;
        preferences.Direction = PdfReadingDirection.RightToLeft;
        preferences.DisablePrintScaling = true;
        preferences.Duplex = PdfDuplexMode.DuplexFlipLongEdge;
        preferences.PrintPageRanges = [(0, 0), (2, 4)];
        preferences.NumberOfCopies = 2;
        #endregion

        var read = PdfDocument.Open(document.ToByteArray());
        Assert.Equal(PdfPageLayout.TwoPageRight, read.PageLayout);
        Assert.Equal(PdfPageMode.UseOutlines, read.PageMode);
        var readPreferences = read.ViewerPreferences;
        Assert.True(readPreferences.DisplayDocumentTitle);
        Assert.True(readPreferences.HideToolbar);
        Assert.False(readPreferences.HideMenubar);
        Assert.Equal(PdfReadingDirection.RightToLeft, readPreferences.Direction);
        Assert.True(readPreferences.DisablePrintScaling);
        Assert.Equal(PdfDuplexMode.DuplexFlipLongEdge, readPreferences.Duplex);
        Assert.Equal([(0, 0), (2, 4)], readPreferences.PrintPageRanges);
        Assert.Equal(2, readPreferences.NumberOfCopies);
        Assert.Equal([1, 1, 3, 5], readPreferences.Dictionary!.Get<PdfArray>(PdfName.Create("PrintPageRange"))!.Select(item => ((PdfNumber)item).DoubleValue));

        readPreferences.HideToolbar = false;
        readPreferences.DisplayDocumentTitle = false;
        readPreferences.Direction = PdfReadingDirection.LeftToRight;
        readPreferences.DisablePrintScaling = false;
        readPreferences.Duplex = PdfDuplexMode.Unspecified;
        readPreferences.PrintPageRanges = [];
        readPreferences.NumberOfCopies = null;
        Assert.Null(readPreferences.Dictionary);
        Assert.Throws<ArgumentOutOfRangeException>(() => readPreferences.NumberOfCopies = 6);
        Assert.Throws<ArgumentOutOfRangeException>(() => readPreferences.NonFullScreenPageMode = PdfPageMode.FullScreen);
    }

    [Fact]
    public void Example_Portfolio()
    {
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4);

        #region Pdf_Portfolio
        var report = document.EmbeddedFiles.Add("report.pdf", "%PDF-1.7"u8);
        var data = document.EmbeddedFiles.Add("data.csv", "a,b"u8);
        var portfolio = document.CreatePortfolio();
        portfolio.View = PdfPortfolioView.Details;
        portfolio.InitialFile = "report.pdf";
        portfolio.AddField("name", "Name", PdfPortfolioFieldType.FileName);
        portfolio.AddField("owner", "Owner");
        portfolio.AddField("reviewed", "Reviewed", PdfPortfolioFieldType.Date);
        portfolio.SetSort(ascending: true, "owner", "name");
        report.SetPortfolioValue("owner", "Finance");
        report.SetPortfolioValue("reviewed", new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero));
        data.SetPortfolioValue("owner", "IT");
        #endregion

        var read = PdfDocument.Open(document.ToByteArray());
        var readPortfolio = Assert.IsType<PdfPortfolio>(read.Portfolio);
        Assert.Equal("report.pdf", readPortfolio.InitialFile);
        Assert.Equal(["name", "owner", "reviewed"], readPortfolio.Fields.Select(field => field.Key));
        Assert.Equal([PdfPortfolioFieldType.FileName, PdfPortfolioFieldType.Text, PdfPortfolioFieldType.Date], readPortfolio.Fields.Select(field => field.Type));
        Assert.Equal(["owner", "name"], readPortfolio.SortFields);
        Assert.True(readPortfolio.SortAscending);
        var readReport = read.EmbeddedFiles.Single(file => file.FileName == "report.pdf");
        Assert.Equal("Finance", readReport.GetPortfolioValue("owner"));
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), readReport.GetPortfolioValue("reviewed"));
        Assert.Null(readReport.GetPortfolioValue("missing"));
        Assert.Same(read.Portfolio!.Dictionary, read.CreatePortfolio().Dictionary);
        Assert.Null(new PdfDocument().Portfolio);
    }

    [Fact]
    public void PortfolioFoldersAreBounded()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /Collection << /View /T /Folders 4 0 R /Schema << /Type /CollectionSchema /size << /Subtype /Size /N (Size) /O 2 /V false /E true >> /n << /Subtype /N /O 1 >> >> /Sort << /S /n /A [false] >> >> >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R >>")
            .Add(4, "<< /Type /Folder /ID 1 /Name (Root) /Child 5 0 R >>")
            .Add(5, "<< /Type /Folder /ID 2 /Name (A) /Next 6 0 R /Child 4 0 R >>")
            .Add(6, "<< /Type /Folder /ID 3 /Name (B) /Next 5 0 R >>");
        var portfolio = PdfDocument.Open(file.ToArray()).Portfolio!;

        Assert.Equal(PdfPortfolioView.Tile, portfolio.View);
        Assert.Equal(["n", "size"], portfolio.Fields.Select(field => field.Key));
        Assert.False(portfolio.Fields[1].IsVisible);
        Assert.True(portfolio.Fields[1].IsEditable);
        Assert.Equal("Size", portfolio.Fields[1].Name);
        Assert.False(portfolio.SortAscending);
        var root = portfolio.RootFolder!;
        Assert.Equal("Root", root.Name);
        Assert.Equal(1, root.Id);
        Assert.Equal(["A", "B"], root.Children.Select(folder => folder.Name));

        // The cycle of the folders ends at the folder already in the tree: A refers back to the root
        var depth = 0;
        for (var folder = root; folder.Children.Count > 0 && depth < 100; folder = folder.Children[0])
        {
            depth++;
        }

        Assert.Equal(1, depth);
    }

    [Fact]
    public void Example_Revisions()
    {
        var original = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R >>")
            .ToArray();
        var updated = HandWrittenPdf.AppendUpdate(original, [(3, "<< /Type /Page /Parent 2 0 R /Rotate 90 >>")], "<< /Size 4 /Root 1 0 R /Prev {prev} >>");
        var bytes = HandWrittenPdf.AppendUpdate(updated, [(4, "<< /Producer (x) >>")], "<< /Size 5 /Root 1 0 R /Info 4 0 R /Prev {prev} >>");
        var document = PdfDocument.Open(bytes);

        #region Pdf_Revisions
        var revisions = document.Revisions;

        // The document as it was before the incremental updates, and what they changed
        var firstVersion = revisions[0].Open();
        var changes = revisions[0].GetModifications();
        #endregion

        Assert.Equal(0, firstVersion.Pages[0].Rotation);
        Assert.NotEmpty(changes);
        Assert.Equal([original.Length, updated.Length, bytes.Length], revisions.Select(revision => revision.Length));
        Assert.Equal([false, false, true], revisions.Select(revision => revision.IsLatest));
        Assert.Equal(0, revisions[0].Open().Pages[0].Rotation);
        Assert.Equal(90, revisions[1].Open().Pages[0].Rotation);
        Assert.NotEmpty(revisions[0].GetModifications());
        Assert.Empty(revisions[2].GetModifications());
        Assert.Empty(new PdfDocument().Revisions);
    }

    [Fact]
    public void ALinearizedFileIsOneRevision()
    {
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4);
        document.Pages.Add(PdfSize.A4);
        var linearized = document.ToByteArray(new PdfSaveOptions { Linearize = true });

        var read = PdfDocument.Open(linearized);

        Assert.Equal(linearized.Length, Assert.Single(read.Revisions).Length);
        using var stream = new MemoryStream();
        read.Information.Title = "Updated";
        read.SaveIncremental(stream);
        var revisions = PdfDocument.Open(stream.ToArray()).Revisions;
        Assert.Equal([linearized.Length, stream.Length], revisions.Select(revision => revision.Length));
    }

    [Fact]
    public void TheActionsThatFollowAScriptKeepTheirDocument()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /OpenAction << /S /JavaScript /JS (x) /Next << /S /GoTo /D [3 0 R /Fit] >> >> >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R >>");
        var document = PdfDocument.Open(file.ToArray());

        var goTo = Assert.IsType<PdfGoToAction>(Assert.Single(document.OpenAction!.Next));

        Assert.Same(document.Pages[0], goTo.Destination!.Page);
    }

    [Fact]
    public void TheActionsOfASequenceAreChecked()
    {
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4);
        var other = new PdfDocument();
        var otherPage = other.Pages.Add(PdfSize.A4);
        var first = PdfAction.Named("NextPage");
        first.SetNext(PdfAction.GoTo(PdfDestination.Fit(otherPage)));

        Assert.Throws<ArgumentException>(() => document.OpenAction = first);

        var a = PdfAction.Named("NextPage");
        var b = PdfAction.Named("PrevPage");
        var c = PdfAction.Named("FirstPage");
        a.SetNext(b);
        b.SetNext(c);
        Assert.Throws<ArgumentException>(() => c.SetNext(a));
        Assert.Throws<ArgumentException>(() => a.SetNext(a));
    }

    [Fact]
    public void AGroupIsHiddenInAConfigurationWithAnUnchangedBaseState()
    {
        var document = new PdfDocument();
        var group = document.OptionalContent.AddGroup("Layer");
        var configuration = document.OptionalContent.DefaultConfiguration!;
        configuration.BaseState = PdfOptionalContentBaseState.Unchanged;

        configuration.SetVisible(group, visible: false);

        Assert.False(configuration.IsVisible(group));
        Assert.Single(configuration.Dictionary.Get<PdfArray>(PdfName.Create("OFF"))!);
    }

    [Fact]
    public void RemovingTheActionOfABookmarkRemovesIt()
    {
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4);
        document.Outlines.Add("Site").Action = PdfAction.OpenUri(new Uri("https://example.com"));
        var read = PdfDocument.Open(document.ToByteArray());

        read.Outlines[0].Action = null;

        var saved = PdfDocument.Open(read.ToByteArray());
        Assert.Null(saved.Outlines[0].Action);
        Assert.Null(saved.Outlines[0].Destination);
    }

    [Fact]
    public void TheEndOfLineAfterTheEndOfFileMarkerIsInTheRevision()
    {
        // The %%EOF marker ends at the end of the first bytes searched, and its CRLF is after them
        var text = Encoding.Latin1.GetString(HandWrittenPdf.CreateSimple().ToArray());
        var startxref = text.LastIndexOf("startxref", StringComparison.Ordinal);
        var xref = int.Parse(text[(startxref + "startxref".Length)..].Trim().Split('\n')[0].Trim(), CultureInfo.InvariantCulture);
        var trailer = text.LastIndexOf("trailer", StringComparison.Ordinal);
        var dictionary = text[trailer..startxref].Replace("<<", "<< /Pad ()", StringComparison.Ordinal);
        var ending = "startxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF";
        var padding = xref + 4096 - (trailer + dictionary.Length + ending.Length);
        dictionary = dictionary.Replace("/Pad ()", "/Pad (" + new string('x', padding) + ")", StringComparison.Ordinal);
        var original = Encoding.Latin1.GetBytes(text[..trailer] + dictionary + ending + "\r\n");
        var updated = HandWrittenPdf.AppendUpdate(original, [(6, "<< /Title (T) >>")], "<< /Size 7 /Root 1 0 R /Info 6 0 R /Prev {prev} >>");

        var revisions = PdfDocument.Open(updated).Revisions;

        Assert.Equal([original.Length, updated.Length], revisions.Select(revision => revision.Length));
    }

    [Theory]
    [InlineData("/Comment (%%EOF)")]
    [InlineData("/Comment (startxref\n0\n%%EOF\n)")]
    [InlineData("/Comment (x) % startxref\n0\n%%EOF\n")]
    public void AnEndOfFileMarkerInATrailerStringDoesNotEndARevision(string entry)
    {
        var original = HandWrittenPdf.CreateSimple().ToArray();
        var updated = HandWrittenPdf.AppendUpdate(original, [(6, "<< /Title (T) >>")], "<< /Size 7 /Root 1 0 R /Info 6 0 R " + entry + " /Prev {prev} >>");

        var revisions = PdfDocument.Open(updated).Revisions;

        Assert.Equal([original.Length, updated.Length], revisions.Select(revision => revision.Length));
        Assert.Equal("T", revisions[1].Open().Information.Title);
    }

    [Fact]
    public void FoldersReferredToSeveralTimesAreReadOnce()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /Collection << /Type /Collection /Folders 4 0 R >> >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R >>")
            .Add(4, "<< /Type /Folder /Name (Root) /Child 5 0 R >>")
            .Add(5, "<< /Type /Folder /Name (A) /Child 7 0 R /Next 6 0 R >>")
            .Add(6, "<< /Type /Folder /Name (B) /Child 7 0 R >>")
            .Add(7, "<< /Type /Folder /Name (C) /Child 5 0 R >>");
        var root = PdfDocument.Open(file.ToArray()).Portfolio!.RootFolder!;

        var names = new List<string>();
        void Walk(PdfPortfolioFolder folder)
        {
            names.Add(folder.Name);
            foreach (var child in folder.Children)
            {
                Walk(child);
            }
        }

        Walk(root);

        Assert.Equal(["Root", "A", "C", "B"], names);
    }

    [Fact]
    public void PortfolioValuesFollowTheTypesOfTheSchema()
    {
        var document = new PdfDocument();
        var file = document.EmbeddedFiles.Add("a.txt", "a"u8);
        var portfolio = document.CreatePortfolio();
        portfolio.AddField("note", "Note");
        portfolio.Fields[0].Order = 10;
        portfolio.AddField("other", "Other").Order = 20;

        file.SetPortfolioValue("note", "D:20260115000000Z");
        var added = portfolio.AddField("date", "Date", PdfPortfolioFieldType.Date);
        file.SetPortfolioValue("date", new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal("D:20260115000000Z", file.GetPortfolioValue("note"));
        Assert.IsType<DateTimeOffset>(file.GetPortfolioValue("date"));
        Assert.Equal(21, added.Order);
        Assert.Equal(["note", "other", "date"], portfolio.Fields.Select(field => field.Key));
    }

    [Fact]
    public void OneCopyIsWritten()
    {
        var document = new PdfDocument();

        document.ViewerPreferences.NumberOfCopies = 1;

        Assert.Equal(1, PdfDocument.Open(document.ToByteArray()).ViewerPreferences.NumberOfCopies);
    }

    private static void AssertPixel(Meziantou.Framework.Imaging.Image<Meziantou.Framework.Imaging.Rgba32> image, int x, int y, int r, int g, int b, int tolerance = 3)
    {
        var pixel = image.Frames[0][x, image.Height - 1 - y];
        Assert.True(Math.Abs(pixel.R - r) <= tolerance && Math.Abs(pixel.G - g) <= tolerance && Math.Abs(pixel.B - b) <= tolerance, $"({x}, {y}): ({pixel.R}, {pixel.G}, {pixel.B}) instead of ({r}, {g}, {b})");
    }

    [Fact]
    public void TheMarkInfoEntriesAreKept()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /MarkInfo << /Marked false /Suspects true /UserProperties true >> >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R >>");
        var document = PdfDocument.Open(file.ToArray());
        document.StructureTree.AddElement("Document");

        var markInfo = PdfDocument.Open(document.ToByteArray()).Catalog.Get<PdfDictionary>(PdfName.Create("MarkInfo"))!;
        Assert.True(markInfo.GetBoolean(PdfName.Create("Marked")));
        Assert.True(markInfo.GetBoolean(PdfName.Create("Suspects")));
        Assert.True(markInfo.GetBoolean(PdfName.Create("UserProperties")));
    }

    [Fact]
    public void AStructureTreeThatIsOnlyReadIsNotRewritten()
    {
        // A tagged document whose parent tree has two leaves
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /StructTreeRoot 10 0 R /MarkInfo << /Marked true >> >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /StructParents 0 /Contents 4 0 R >>")
            .Add(4, HandWrittenPdf.Stream("/P << /MCID 0 >> BDC EMC"))
            .Add(10, "<< /Type /StructTreeRoot /K 11 0 R /ParentTree 12 0 R /ParentTreeNextKey 1 >>")
            .Add(11, "<< /Type /StructElem /S /P /P 10 0 R /Pg 3 0 R /K 0 >>")
            .Add(12, "<< /Kids [13 0 R] >>")
            .Add(13, "<< /Limits [0 0] /Nums [0 [11 0 R]] >>");
        var original = file.ToArray();
        var document = PdfDocument.Open(original);
        Assert.Equal("P", document.StructureTree.Elements[0].Type);
        Assert.Equal("P", document.StructureTree.Elements[0].Type);
        document.Information.Title = "Changed";

        using var output = new MemoryStream();
        document.SaveIncremental(output);
        var update = Encoding.Latin1.GetString(output.ToArray()[original.Length..]);
        Assert.DoesNotContain("ParentTree", update, StringComparison.Ordinal);
        Assert.DoesNotContain("Nums", update, StringComparison.Ordinal);
        Assert.DoesNotContain("/Catalog", update, StringComparison.Ordinal);

        // A change is still written
        var changed = PdfDocument.Open(output.ToArray());
        changed.StructureTree.Elements[0].AddElement("Span");
        using var second = new MemoryStream();
        changed.SaveIncremental(second);
        Assert.Contains("/Nums", Encoding.Latin1.GetString(second.ToArray()[output.ToArray().Length..]), StringComparison.Ordinal);
    }

    private static PdfString Parse(string value)
    {
        if (value[0] == '(')
            return PdfString.FromBytes(Encoding.Latin1.GetBytes(value[1..^1]));

        return PdfString.FromBytes(Convert.FromHexString(value[1..^1]), hexadecimal: true);
    }

    /// <summary>Adds a link to the first page of a document and saves it.</summary>
    private static byte[] Link(PdfDocument document, PdfDictionary link)
    {
        link[PdfNames.Subtype] = PdfNames.Link;
        link[PdfNames.Rect] = PdfArray.FromNumbers(0, 0, 1, 1);
        document.Pages[0].Dictionary[PdfNames.Annots] = new PdfArray(document.Objects.Add(link));
        return document.ToByteArray();
    }
}
