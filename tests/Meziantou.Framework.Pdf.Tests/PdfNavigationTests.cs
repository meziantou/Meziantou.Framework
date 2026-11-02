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

        var square = Assert.IsType<PdfMarkupAnnotation>(annotations[0]);
        Assert.Equal("Ann", square.Author);
        Assert.Equal("Shape", square.Subject);
        Assert.Equal(0.5, square.Opacity);
        Assert.Equal(PdfColor.FromRgb(0, 0, 1), square.Color);
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero), square.CreationDate);
        Assert.IsNotType<PdfMarkupAnnotation>(annotations[1]);
        Assert.Null(annotations[1].Color);

        square.Opacity = 1;
        square.Color = null;
        square.CreationDate = null;
        Assert.False(square.Dictionary.ContainsKey(PdfName.Create("CA")));
        Assert.False(square.Dictionary.ContainsKey(PdfNames.C));
        Assert.Throws<ArgumentOutOfRangeException>(() => square.Opacity = 2);
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
