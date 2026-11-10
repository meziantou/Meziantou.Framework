using System.Text;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering;

namespace Meziantou.Framework.Pdf.Tests;

public sealed class PdfPageTests
{
    private static readonly PdfName StructParents = PdfName.Create("StructParents");

    /// <summary>A document whose page tree node rotates its pages and crops them.</summary>
    private static PdfDocument CreateRotatedTree()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /Rotate 90 /CropBox [10 10 100 100] /MediaBox [0 0 200 200] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R >>");
        return PdfDocument.Open(file.ToArray());
    }

    [Fact]
    public void AddedPagesDoNotInheritTheRotationAndTheCropBox()
    {
        var document = CreateRotatedTree();
        var added = document.Pages.Add(PdfSize.A4);
        var inserted = document.Pages.Insert(0, new PdfSize(300, 400));

        Assert.Equal(90, document.Pages[1].Rotation);
        Assert.Equal(new PdfRectangle(10, 10, 100, 100), document.Pages[1].CropBox);
        foreach (var page in new[] { added, inserted })
        {
            Assert.Equal(0, page.Rotation);
            Assert.Equal(page.MediaBox, page.CropBox);
            Assert.NotSame(document.Pages[1].Resources, page.Resources);
        }

        var read = PdfDocument.Open(document.ToByteArray());
        Assert.Equal([0, 90, 0], read.Pages.Select(page => page.Rotation));
        Assert.Equal(new PdfRectangle(0, 0, 300, 400), read.Pages[0].CropBox);
        Assert.Equal(PdfSize.A4.Width, read.Pages[2].CropBox.Width, 0.001);
    }

    [Fact]
    public void ANewPageOfAPlainTreeHasNoRotationNorCropBoxEntry()
    {
        var page = new PdfDocument().Pages.Add(PdfSize.A4);

        Assert.False(page.Dictionary.ContainsKey(PdfNames.Rotate));
        Assert.False(page.Dictionary.ContainsKey(PdfNames.CropBox));
    }

    [Fact]
    public void EachContentBuilderStartsWithTheDefaultGraphicsState()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(100, 100));
        page.CreateContentBuilder().Translate(50, 50).SetFillRgb(0, 1, 0);
        page.CreateContentBuilder().Rectangle(0, 0, 40, 100).Clip().EndPath();
        page.CreateContentBuilder().Rectangle(0, 0, 10, 10).Fill();
        page.CreateContentBuilder().Rectangle(80, 80, 10, 10).Fill();

        var streams = page.Dictionary.Get<PdfArray>(PdfNames.Contents)!.Select(item => Encoding.ASCII.GetString(((PdfStream)PdfReference.ResolveIfReference(item)).GetDecodedData())).ToList();
        Assert.HasCount(5, streams);
        Assert.Equal("q\nq\nq\n", streams[0]);
        Assert.All(streams.Skip(2), stream => Assert.StartsWith("Q\n", stream, StringComparison.Ordinal));
        using var image = page.Render(new PdfRenderOptions { UseSystemFonts = false });

        // Black (not translated, not green), and not clipped by the second builder
        AssertColor(Pixel(image, 5, 5), 0, 0, 0);
        AssertColor(Pixel(image, 85, 85), 0, 0, 0);
        AssertColor(Pixel(image, 55, 55), 255, 255, 255);
    }

    [Fact]
    public void ContentBuildersOfAPageReadAreIsolated()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R >>")
            .Add(4, HandWrittenPdf.Stream("50 50 cm"));
        var document = PdfDocument.Open(file.ToArray());
        document.Pages[0].CreateContentBuilder().Translate(10, 10);
        document.Pages[0].CreateContentBuilder().Rectangle(0, 0, 10, 10).Fill();

        var read = PdfDocument.Open(document.ToByteArray());
        using var image = read.Pages[0].Render(new PdfRenderOptions { UseSystemFonts = false });
        AssertColor(Pixel(image, 5, 5), 0, 0, 0);
        AssertColor(Pixel(image, 65, 65), 255, 255, 255);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("1e400")]
    [InlineData("(text)")]
    public void AnInvalidUserUnitIsTheDefault(string value)
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 50] /UserUnit " + value + " >>");
        var page = PdfDocument.Open(file.ToArray()).Pages[0];

        Assert.Equal(1, page.UserUnit);
        using var image = page.Render(new PdfRenderOptions { UseSystemFonts = false });
        Assert.Equal(100, image.Width);
        Assert.Equal(50, image.Height);
    }

    [Fact]
    public void AValidUserUnitScalesThePage()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 50] /UserUnit 2 >>");
        var page = PdfDocument.Open(file.ToArray()).Pages[0];

        Assert.Equal(2, page.UserUnit);
        Assert.Equal(new PdfSize(200, 100), page.DisplaySize);
    }

    [Fact]
    public void ResourcesSharedByPagesAreCopiedBeforeTheyChange()
    {
        // The pages share an indirect resource dictionary, and the second one an indirect font category with the first
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R 4 0 R 5 0 R] /Count 3 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources 6 0 R >>")
            .Add(4, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources 6 0 R >>")
            .Add(5, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << /Font 7 0 R >> >>")
            .Add(6, "<< /Font 7 0 R >>")
            .Add(7, "<< /F1 8 0 R >>")
            .Add(8, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        var original = file.ToArray();
        var document = PdfDocument.Open(original);
        var courier = PdfFont.GetStandardFont(PdfStandardFont.Courier);
        document.Pages[0].CreateContentBuilder().DrawText("A", courier, 10, 0, 0);
        document.Pages[2].CreateContentBuilder().DrawText("B", courier, 10, 0, 0);

        using var output = new MemoryStream();
        document.SaveIncremental(output);
        Assert.Equal(original, output.ToArray()[..original.Length]);

        var read = PdfDocument.Open(output.ToArray());
        Assert.Equal(2, FontCount(read.Pages[0]));
        Assert.Equal(1, FontCount(read.Pages[1]));
        Assert.Equal(2, FontCount(read.Pages[2]));
        Assert.Equal(1, read.Objects.Resolve(read.Pages[1].Dictionary.Get<PdfDictionary>(PdfNames.Resources)![PdfNames.Font] as PdfReference ?? throw new InvalidOperationException()) is PdfDictionary fonts ? fonts.Count : 0);

        // The font the pages shared is still shared
        Assert.Equal(
            read.Pages[0].Resources!.Get<PdfDictionary>(PdfNames.Font)![PdfName.Create("F1")],
            read.Pages[1].Resources!.Get<PdfDictionary>(PdfNames.Font)![PdfName.Create("F1")]);

        static int FontCount(PdfPage page) => page.Resources!.Get<PdfDictionary>(PdfNames.Font)!.Count;
    }

    [Fact]
    public void RemovingAPageRemovesTheReferencesToIt()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /Outlines 10 0 R /OpenAction [4 0 R /Fit] /Dests 20 0 R /Names << /Dests 21 0 R >> >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Annots [5 0 R 6 0 R 7 0 R] >>")
            .Add(4, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>")
            .Add(5, "<< /Type /Annot /Subtype /Link /Rect [0 0 10 10] /Dest [4 0 R /Fit] >>")
            .Add(6, "<< /Type /Annot /Subtype /Link /Rect [0 0 10 10] /A << /S /GoTo /D [4 0 R /XYZ 0 0 0] >> >>")
            .Add(7, "<< /Type /Annot /Subtype /Link /Rect [0 0 10 10] /A << /S /URI /URI (https://example.com) >> >>")
            .Add(10, "<< /Type /Outlines /First 11 0 R /Last 12 0 R /Count 2 >>")
            .Add(11, "<< /Title (One) /Parent 10 0 R /Next 12 0 R /Dest [3 0 R /Fit] >>")
            .Add(12, "<< /Title (Two) /Parent 10 0 R /Prev 11 0 R /Dest [4 0 R /Fit] >>")
            .Add(20, "<< /old [4 0 R /Fit] /kept [3 0 R /Fit] >>")
            .Add(21, "<< /Names [(a) [3 0 R /Fit] (b) << /D [4 0 R /Fit] >>] >>");
        var original = file.ToArray();

        foreach (var incremental in new[] { false, true })
        {
            var document = PdfDocument.Open(original);
            var removed = document.Pages[1].Reference;
            document.Pages.RemoveAt(1);

            using var output = new MemoryStream();
            if (incremental)
            {
                document.SaveIncremental(output);
            }
            else
            {
                document.Save(output);
            }

            var read = PdfDocument.Open(output.ToArray());
            Assert.Single(read.Pages);
            Assert.False(read.Catalog.ContainsKey(PdfName.Create("OpenAction")));
            Assert.Equal(["One", "Two"], read.Outlines.Select(item => item.Title));
            Assert.NotNull(read.Outlines[0].Destination);
            Assert.Null(read.Outlines[1].Destination);
            var links = read.Pages[0].Annotations.OfType<PdfLinkAnnotation>().ToList();
            Assert.HasCount(3, links);
            Assert.Null(links[0].Destination);
            Assert.Null(links[1].Destination);
            Assert.False(links[1].Dictionary.ContainsKey(PdfNames.A));
            Assert.Equal(new Uri("https://example.com"), links[2].Uri);
            Assert.Equal([PdfName.Create("kept")], read.Catalog.Get<PdfDictionary>(PdfNames.Dests)!.Keys);
            Assert.HasCount(2, read.Catalog.Get<PdfDictionary>(PdfNames.Names)!.Get<PdfDictionary>(PdfNames.Dests)!.Get<PdfArray>(PdfNames.Names)!);

            if (!incremental)
            {
                // The page is not written anymore
                Assert.DoesNotContain(read.Objects.GetReferences(), reference => read.Objects.Resolve(reference) is PdfDictionary { Count: > 0 } page && page.IsType(PdfNames.Page) && page != read.Pages[0].Dictionary);
            }

            _ = removed;
        }
    }

    [Fact]
    public void RemovingAPageRemovesItsContentFromTheStructureTree()
    {
        var document = new PdfDocument();
        var root = document.StructureTree.AddElement("Document");
        var first = root.AddElement("P");
        var second = root.AddElement("P");
        var font = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);
        var page1 = document.Pages.Add(PdfSize.A5);
        var page2 = document.Pages.Add(PdfSize.A5);
        page1.CreateContentBuilder().BeginStructureElement(first).DrawText("One", font, 10, 10, 10).EndMarkedContent();
        page2.CreateContentBuilder().BeginStructureElement(second).DrawText("Two", font, 10, 10, 10).EndMarkedContent();
        var link = page2.Annotations.AddLink(new PdfRectangle(0, 0, 10, 10), new Uri("https://example.com"));
        second.AddAnnotation(link);

        document.Pages.RemoveAt(1);
        var read = PdfDocument.Open(document.ToByteArray());

        Assert.Single(read.Pages);
        var elements = read.StructureTree.Elements[0].Children;
        Assert.HasCount(2, elements);
        Assert.NotNull(elements[0].Dictionary[PdfNames.K]);
        Assert.Empty(elements[1].Dictionary.Get<PdfArray>(PdfNames.K)!);
        var nums = read.StructureTree.Elements[0].Dictionary.Get<PdfDictionary>(PdfNames.P)!.Get<PdfDictionary>(PdfName.Create("ParentTree"))!.Get<PdfArray>(PdfName.Create("Nums"))!;
        Assert.HasCount(2, nums);
    }

    [Fact]
    public void PageBoxes()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(600, 800));
        page.CropBox = new PdfRectangle(10, 10, 590, 790);

        Assert.Equal(page.CropBox, page.BleedBox);
        Assert.Equal(page.CropBox, page.TrimBox);
        Assert.Equal(page.CropBox, page.ArtBox);

        page.BleedBox = new PdfRectangle(20, 20, 580, 780);
        page.TrimBox = new PdfRectangle(30, 30, 570, 770);
        page.ArtBox = new PdfRectangle(40, 40, 560, 760);
        page.Rotation = 90;
        Assert.Throws<ArgumentException>(() => page.TrimBox = new PdfRectangle(0, 0, double.NaN, 1));
        Assert.Throws<ArgumentException>(() => page.MediaBox = new PdfRectangle(0, 0, double.PositiveInfinity, 1));

        var read = PdfDocument.Open(document.ToByteArray()).Pages[0];
        Assert.Equal(new PdfRectangle(20, 20, 580, 780), read.BleedBox);
        Assert.Equal(new PdfRectangle(30, 30, 570, 770), read.TrimBox);
        Assert.Equal(new PdfRectangle(40, 40, 560, 760), read.ArtBox);
        Assert.Equal(new PdfRectangle(10, 10, 590, 790), read.CropBox);
        Assert.Equal(90, read.Rotation);
    }

    [Fact]
    public void TheBoxesOfAFileAreRead()
    {
        // The boxes are not inherited (Table 31): the node's trim box is ignored
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /TrimBox [1 1 2 2] /CropBox [5 5 95 95] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /ArtBox [10 20 30 40] >>");
        var page = PdfDocument.Open(file.ToArray()).Pages[0];

        Assert.Equal(new PdfRectangle(5, 5, 95, 95), page.TrimBox);
        Assert.Equal(new PdfRectangle(5, 5, 95, 95), page.BleedBox);
        Assert.Equal(new PdfRectangle(10, 20, 30, 40), page.ArtBox);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void DisplayCoordinates(int rotation)
    {
        // A page cropped at a nonzero origin: a mark drawn at the top-left corner of the display appears there
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(300, 200));
        page.CropBox = new PdfRectangle(50, 20, 250, 120);
        page.Rotation = rotation;

        var size = page.DisplaySize;
        Assert.Equal(rotation is 90 or 270 ? new PdfSize(100, 200) : new PdfSize(200, 100), size);
        page.CreateContentBuilder()
            .Transform(page.DisplayToUserSpace)
            .SetFillRgb(1, 0, 0)
            .Rectangle(0, size.Height - 10, 10, 10)
            .Fill();

        // The corners of the display space are the corners of the crop box
        var corners = new[] { (0d, 0d), (size.Width, 0d), (0d, size.Height), (size.Width, size.Height) }
            .Select(point => page.DisplayToUserSpace.Transform(point.Item1, point.Item2))
            .Select(point => (Math.Round(point.X, 6, MidpointRounding.AwayFromZero), Math.Round(point.Y, 6, MidpointRounding.AwayFromZero)))
            .ToHashSet();
        Assert.True(corners.SetEquals([(50, 20), (250, 20), (50, 120), (250, 120)]));
        var back = page.UserSpaceToDisplay.Transform(page.DisplayToUserSpace.Transform(12, 34).X, page.DisplayToUserSpace.Transform(12, 34).Y);
        Assert.Equal(12, back.X, 6);
        Assert.Equal(34, back.Y, 6);

        using var image = page.Render(new PdfRenderOptions { UseSystemFonts = false });
        Assert.Equal((int)size.Width, image.Width);
        AssertColor(image.Frames[0][5, 5], 255, 0, 0);
        AssertColor(image.Frames[0][image.Width - 5, image.Height - 5], 255, 255, 255);
    }

    [Fact]
    public void DisplayCoordinatesAreInPointsWithAUserUnit()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 50] /UserUnit 3 /Rotate 90 >>");
        var page = PdfDocument.Open(file.ToArray()).Pages[0];

        Assert.Equal(new PdfSize(150, 300), page.DisplaySize);
        var (x, y) = page.DisplayToUserSpace.Transform(150, 300);
        Assert.Equal(0, x, 6);
        Assert.Equal(50, y, 6);
    }

    [Fact]
    public void MovePages()
    {
        // A tree of two nodes: the second rotates its pages
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 3 /MediaBox [0 0 100 100] /Resources << /Font << /F1 9 0 R >> >> >>")
            .Add(3, "<< /Type /Pages /Parent 2 0 R /Kids [5 0 R 6 0 R] /Count 2 >>")
            .Add(4, "<< /Type /Pages /Parent 2 0 R /Kids [7 0 R] /Count 1 /Rotate 90 >>")
            .Add(5, "<< /Type /Page /Parent 3 0 R /Contents 10 0 R >>")
            .Add(6, "<< /Type /Page /Parent 3 0 R /Contents 11 0 R >>")
            .Add(7, "<< /Type /Page /Parent 4 0 R /Contents 12 0 R >>")
            .Add(9, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>")
            .Add(10, HandWrittenPdf.Stream("BT /F1 10 Tf (A) Tj ET"))
            .Add(11, HandWrittenPdf.Stream("BT /F1 10 Tf (B) Tj ET"))
            .Add(12, HandWrittenPdf.Stream("BT /F1 10 Tf (C) Tj ET"));
        var original = file.ToArray();
        var document = PdfDocument.Open(original);
        var a = document.Pages[0];

        document.Pages.Move(0, 2);
        document.Pages.Move(2, 1);
        document.Pages.Move(0, 2);

        Assert.Equal(["A", "C", "B"], document.Pages.Select(page => page.ExtractText()));
        Assert.Same(a, document.Pages[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Pages.Move(0, 3));

        using var output = new MemoryStream();
        document.SaveIncremental(output);
        var read = PdfDocument.Open(output.ToArray());
        Assert.Equal(["A", "C", "B"], read.Pages.Select(page => page.ExtractText()));
        Assert.Equal([0, 90, 0], read.Pages.Select(page => page.Rotation));
        Assert.Equal(3, read.Catalog.Get<PdfDictionary>(PdfNames.Pages)!.GetInteger(PdfNames.Count));
        Assert.Equal(3, read.Pages[1].Dictionary.Get<PdfDictionary>(PdfNames.Parent)!.GetInteger(PdfNames.Count));
        Assert.Equal(0, read.Objects.Resolve(read.Catalog.Get<PdfDictionary>(PdfNames.Pages)!.Get<PdfArray>(PdfNames.Kids)![0] as PdfReference ?? throw new InvalidOperationException()) is PdfDictionary first ? first.GetInteger(PdfNames.Count) : -1);
    }

    [Fact]
    public void MovingAPageKeepsTheDestinationsToIt()
    {
        var document = new PdfDocument();
        var first = document.Pages.Add(PdfSize.A5);
        var second = document.Pages.Add(PdfSize.A5);
        document.Outlines.Add("Second", PdfDestination.Fit(second));
        first.Annotations.AddLink(new PdfRectangle(0, 0, 10, 10), PdfDestination.Fit(second));

        document.Pages.Move(1, 0);
        var read = PdfDocument.Open(document.ToByteArray());

        Assert.Same(read.Pages[0], read.Outlines[0].Destination!.Page);
        Assert.Same(read.Pages[0], read.Pages[1].Annotations.OfType<PdfLinkAnnotation>().Single().Destination!.Page);
    }

    [Fact]
    public void DuplicatePages()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /Rotate 180 /Resources << /Font << /F1 5 0 R >> >> >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /StructParents 0 /Annots [6 0 R 7 0 R] >>")
            .Add(4, HandWrittenPdf.Stream("BT /F1 10 Tf (Page) Tj ET"))
            .Add(5, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>")
            .Add(6, "<< /Type /Annot /Subtype /Text /Rect [0 0 10 10] /Contents (Note) /P 3 0 R /NM (n1) >>")
            .Add(7, "<< /Type /Annot /Subtype /Widget /FT /Tx /T (field) /Rect [0 0 10 10] /P 3 0 R >>");
        var original = file.ToArray();
        var document = PdfDocument.Open(original);

        var copy = document.Pages.Duplicate(0);
        copy.CreateContentBuilder().DrawText("Copy", PdfFont.GetStandardFont(PdfStandardFont.Courier), 10, 0, 50);

        using var output = new MemoryStream();
        document.SaveIncremental(output);
        var read = PdfDocument.Open(output.ToArray());
        Assert.HasCount(2, read.Pages);
        Assert.Equal("Page", read.Pages[0].ExtractText());
        Assert.Equal("Page\nCopy", read.Pages[1].ExtractText());
        Assert.Equal(180, read.Pages[1].Rotation);
        Assert.False(read.Pages[1].Dictionary.ContainsKey(StructParents));
        Assert.HasCount(1, read.Pages[0].Resources!.Get<PdfDictionary>(PdfNames.Font)!);

        // The note is copied, the widget is not
        var annotation = Assert.Single(read.Pages[1].Annotations);
        Assert.Equal("Note", annotation.Contents);
        Assert.Equal(read.Pages[1].Reference, annotation.Dictionary[PdfNames.P]);
        Assert.False(annotation.Dictionary.ContainsKey(PdfName.Create("NM")));
        Assert.HasCount(2, read.Pages[0].Annotations);
    }

    [Fact]
    public void ImportPages()
    {
        var font = PdfFont.Load(File.ReadAllBytes(TestHarness.RepositoryPaths.Fonts / "noto-sans" / "NotoSans-Regular.subset.ttf"));
        var source = new PdfDocument();
        var sourcePage1 = source.Pages.Add(new PdfSize(200, 100));
        var sourcePage2 = source.Pages.Add(new PdfSize(300, 100));
        sourcePage1.CreateContentBuilder().DrawText("First", font, 12, 10, 10);
        sourcePage2.CreateContentBuilder().DrawText("Second", font, 12, 10, 10);
        sourcePage1.Annotations.AddLink(new PdfRectangle(0, 0, 10, 10), PdfDestination.Fit(sourcePage2));
        sourcePage2.Annotations.AddLink(new PdfRectangle(0, 0, 10, 10), PdfDestination.Fit(sourcePage1));
        sourcePage2.Rotation = 90;

        var target = PdfDocument.Open(HandWrittenPdf.CreateSimple().ToArray());
        var imported1 = target.Pages.Add(sourcePage1);
        var imported2 = target.Pages.Add(sourcePage2);
        target.Pages.Insert(0, sourcePage1);

        var read = PdfDocument.Open(target.ToByteArray());
        Assert.Equal(["First", "Hello", "First", "Second"], read.Pages.Select(page => page.ExtractText()));
        Assert.Equal(90, read.Pages[3].Rotation);
        Assert.Equal(new PdfRectangle(0, 0, 300, 100), read.Pages[3].MediaBox);

        // The links go to the copies (the copy made when the link was), forward links included; the font is shared by the copies of a document
        Assert.Same(read.Pages[3], read.Pages[2].Annotations.OfType<PdfLinkAnnotation>().Single().Destination!.Page);
        Assert.Same(read.Pages[2], read.Pages[3].Annotations.OfType<PdfLinkAnnotation>().Single().Destination!.Page);
        Assert.Same(read.Pages[3], read.Pages[0].Annotations.OfType<PdfLinkAnnotation>().Single().Destination!.Page);
        Assert.Equal(read.Pages[0].Resources!.Get<PdfDictionary>(PdfNames.Font)!.First().Value, read.Pages[3].Resources!.Get<PdfDictionary>(PdfNames.Font)!.First().Value);
        Assert.NotSame(imported1, imported2);
    }

    [Fact]
    public void ImportPagesOfAnEncryptedDocument()
    {
        var source = new PdfDocument { Encryption = new PdfEncryption("user", "owner") };
        source.Pages.Add(PdfSize.A5).CreateContentBuilder().DrawText("Secret", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 12, 10, 10);
        var encrypted = PdfDocument.Open(source.ToByteArray(), new PdfReadOptions { Password = "user" });

        var target = new PdfDocument();
        target.Pages.Add(encrypted.Pages[0]);

        var read = PdfDocument.Open(target.ToByteArray());
        Assert.False(read.IsEncrypted);
        Assert.Equal("Secret", read.Pages[0].ExtractText());
    }

    [Fact]
    public void ImportResolvesTheNamedDestinations()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /Names << /Dests << /Names [(target) [4 0 R /Fit]] >> >> >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 /MediaBox [0 0 100 100] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Annots [5 0 R] >>")
            .Add(4, "<< /Type /Page /Parent 2 0 R >>")
            .Add(5, "<< /Type /Annot /Subtype /Link /Rect [0 0 10 10] /Dest (target) >>");
        var source = PdfDocument.Open(file.ToArray());

        var target = new PdfDocument();
        target.Pages.Add(source.Pages[0]);
        target.Pages.Add(source.Pages[1]);

        var read = PdfDocument.Open(target.ToByteArray());
        Assert.Same(read.Pages[1], read.Pages[0].Annotations.OfType<PdfLinkAnnotation>().Single().Destination!.Page);
    }

    #region Pdf_MergeDocuments
    public static byte[] Merge(IEnumerable<byte[]> files)
    {
        var result = new PdfDocument();
        foreach (var file in files)
        {
            // The source documents must stay open until the result is saved
            var source = PdfDocument.Open(file);
            foreach (var page in source.Pages)
            {
                result.Pages.Add(page);
            }
        }

        return result.ToByteArray();
    }
    #endregion

    [Fact]
    public void Example_MergeDocuments()
    {
        var merged = Merge([HandWrittenPdf.CreateSimple("One").ToArray(), HandWrittenPdf.CreateSimple("Two").ToArray()]);

        Assert.Equal(["One", "Two"], PdfDocument.Open(merged).Pages.Select(page => page.ExtractText()));
    }

    [Fact]
    public void Example_ReorderPages()
    {
        var document = new PdfDocument();
        var font = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);
        foreach (var text in new[] { "Cover", "Chapter", "Contents" })
        {
            document.Pages.Add(PdfSize.A5).CreateContentBuilder().DrawText(text, font, 12, 20, 20);
        }

        #region Pdf_ReorderPages
        // Move the table of contents after the cover, and repeat the cover at the end
        document.Pages.Move(2, 1);
        document.Pages.Insert(document.Pages.Count, document.Pages[0]);
        #endregion

        Assert.Equal(["Cover", "Contents", "Chapter", "Cover"], document.Pages.Select(page => page.ExtractText()));
    }

    [Fact]
    public void Example_DisplayCoordinates()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(PdfSize.A4);
        page.Rotation = 90;

        #region Pdf_DisplayCoordinates
        // Draw a stamp at the top-left corner of the page as it is displayed, whatever its rotation and crop box
        var size = page.DisplaySize;
        page.CreateContentBuilder()
            .Transform(page.DisplayToUserSpace)
            .DrawText("DRAFT", PdfFont.GetStandardFont(PdfStandardFont.HelveticaBold), 24, 20, size.Height - 40);
        #endregion

        Assert.Equal(PdfSize.A4.Height, size.Width, 6);
        Assert.Equal("DRAFT", PdfDocument.Open(document.ToByteArray()).Pages[0].ExtractText());
    }

    [Fact]
    public void Example_PageLabels()
    {
        var document = new PdfDocument();
        for (var i = 0; i < 8; i++)
        {
            document.Pages.Add(PdfSize.A5);
        }

        #region Pdf_PageLabels
        // i, ii, iii for the front matter, then 1, 2, 3... and A-1, A-2 for the appendix
        document.PageLabels.Set(0, new PdfPageLabel(PdfPageLabelStyle.LowercaseRoman));
        document.PageLabels.Set(3, new PdfPageLabel(PdfPageLabelStyle.Decimal));
        document.PageLabels.Set(6, new PdfPageLabel(PdfPageLabelStyle.Decimal, prefix: "A-"));
        #endregion

        var read = PdfDocument.Open(document.ToByteArray());
        Assert.Equal(["i", "ii", "iii", "1", "2", "3", "A-1", "A-2"], read.Pages.Select(page => page.Label));
        Assert.Equal([0, 3, 6], read.PageLabels.Select(range => range.Key));
    }

    [Fact]
    public void PageLabelsOfAFileAreRead()
    {
        // A number tree of two leaves (7.9.7), the styles of table 161, a start number, a range without style
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /PageLabels 10 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R 4 0 R 5 0 R 6 0 R 7 0 R 8 0 R] /Count 6 /MediaBox [0 0 100 100] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R >>").Add(4, "<< /Type /Page /Parent 2 0 R >>").Add(5, "<< /Type /Page /Parent 2 0 R >>")
            .Add(6, "<< /Type /Page /Parent 2 0 R >>").Add(7, "<< /Type /Page /Parent 2 0 R >>").Add(8, "<< /Type /Page /Parent 2 0 R >>")
            .Add(10, "<< /Kids [11 0 R 12 0 R] >>")
            .Add(11, "<< /Limits [0 2] /Nums [0 << /S /R /St 4 >> 2 << /P (Cover) >>] >>")
            .Add(12, "<< /Limits [3 5] /Nums [3 << /S /a /St 26 >> 5 << /S /D /P (p.) /St 10 >>] >>");
        var document = PdfDocument.Open(file.ToArray());

        Assert.Equal(["IV", "V", "Cover", "z", "aa", "p.10"], document.Pages.Select(page => page.Label));
        Assert.Equal(new PdfPageLabel(PdfPageLabelStyle.LowercaseLetters, startNumber: 26), document.PageLabels.First(range => range.Key == 3).Value);

        // A change rewrites the tree as one node
        Assert.True(document.PageLabels.Remove(2));
        Assert.False(document.PageLabels.Remove(1));
        var read = PdfDocument.Open(document.ToByteArray());
        Assert.Equal(["IV", "V", "VI", "z", "aa", "p.10"], read.Pages.Select(page => page.Label));
        read.PageLabels.Clear();
        Assert.Null(read.Pages[0].Label);
        Assert.False(read.Catalog.ContainsKey(PdfName.Create("PageLabels")));
    }

    [Theory]
    [InlineData(PdfPageLabelStyle.UppercaseRoman, 1994, "MCMXCIV")]
    [InlineData(PdfPageLabelStyle.LowercaseRoman, 49, "xlix")]
    [InlineData(PdfPageLabelStyle.UppercaseLetters, 1, "A")]
    [InlineData(PdfPageLabelStyle.UppercaseLetters, 27, "AA")]
    [InlineData(PdfPageLabelStyle.LowercaseLetters, 53, "aaa")]
    [InlineData(PdfPageLabelStyle.LowercaseLetters, 52, "zz")]
    [InlineData(PdfPageLabelStyle.None, 5, "")]
    public void PageLabelStyles(PdfPageLabelStyle style, int number, string expected)
    {
        Assert.Equal(expected, new PdfPageLabel(style, startNumber: number).GetLabel(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfPageLabel(style, startNumber: 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    public void Example_StampAPageOnOtherPages(int rotation)
    {
        var letterhead = new PdfDocument();
        var source = letterhead.Pages.Add(new PdfSize(200, 100));
        source.CropBox = new PdfRectangle(50, 0, 150, 100);
        source.Rotation = rotation;
        source.CreateContentBuilder().SetFillRgb(1, 0, 0).Rectangle(50, 0, 20, 20).Fill();
        var document = new PdfDocument();
        document.Pages.Add(new PdfSize(200, 200));
        document.Pages.Add(new PdfSize(200, 200));
        var sourcePage = letterhead.Pages[0];

        #region Pdf_StampPage
        // The page of another document becomes a form: painted on each page, under or over their content
        var form = document.CreateFormFromPage(sourcePage);
        foreach (var page in document.Pages)
        {
            page.CreateContentBuilder().DrawForm(form);
        }
        #endregion

        Assert.Equal(sourcePage.VisibleBox, form.BoundingBox);
        var read = PdfDocument.Open(document.ToByteArray());
        foreach (var page in read.Pages)
        {
            // The lower-left corner of the source, at the origin; turned clockwise to the top-left corner of the display when the page is rotated
            using var image = page.Render(new PdfRenderOptions { UseSystemFonts = false });
            if (rotation == 0)
            {
                AssertColor(Pixel(image, 5, 5), 255, 0, 0);
            }
            else
            {
                AssertColor(Pixel(image, 5, 95), 255, 0, 0);
                Assert.Equal(new PdfSize(100, 100), sourcePage.DisplaySize);
            }

            AssertColor(Pixel(image, 150, 150), 255, 255, 255);
        }

        Assert.Equal(read.Pages[0].Resources!.Get<PdfDictionary>(PdfNames.XObject)!.First().Value, read.Pages[1].Resources!.Get<PdfDictionary>(PdfNames.XObject)!.First().Value);
    }

    [Fact]
    public void Example_ExtractImages()
    {
        using var pixels = Image.ImportPixelData<Rgba32>([new Rgba32(255, 0, 0, 255), new Rgba32(0, 0, 255, 128)], 2, 1);
        var pdfImage = PdfImage.FromImage(pixels);
        var document = new PdfDocument();
        var builder = document.Pages.Add(PdfSize.A5).CreateContentBuilder().DrawImage(pdfImage, 10, 20, 100, 50);
        var logo = document.CreateForm(new PdfRectangle(0, 0, 10, 10));
        logo.CreateContentBuilder().DrawImage(pdfImage, 0, 0, 10, 10);
        builder.Translate(200, 300).DrawForm(logo);

        // An inline image: one gray sample, in hexadecimal, painted in a 5 x 5 square
        builder.AppendRawContent("q 5 0 0 5 300 300 cm BI /W 1 /H 1 /BPC 8 /CS /G /F /AHx ID 80> EI Q"u8);
        var page = PdfDocument.Open(document.ToByteArray()).Pages[0];

        #region Pdf_ExtractImages
        foreach (var image in page.GetImages())
        {
            // Where the image is on the page, and its pixels, with its color space and its soft mask applied
            var bounds = image.Bounds;
            using var decoded = image.Decode();
        }
        #endregion

        var images = page.GetImages();
        Assert.HasCount(3, images);
        Assert.Equal(new PdfRectangle(10, 20, 110, 70), images[0].Bounds);
        Assert.Equal(new PdfRectangle(200, 300, 210, 310), images[1].Bounds);
        Assert.Equal(images[0].Reference, images[1].Reference);
        Assert.False(images[0].IsInline);
        Assert.Equal("Im1", images[0].Name);
        Assert.Equal(2, images[0].Width);
        Assert.Equal(PdfNames.DeviceRGB, images[0].ColorSpace);
        Assert.NotNull(images[0].SoftMask);
        using (var decoded = images[0].Decode()!)
        {
            Assert.Equal(new Rgba32(255, 0, 0, 255), decoded.Frames[0][0, 0]);
            Assert.Equal(new Rgba32(0, 0, 255, 128), decoded.Frames[0][1, 0]);
        }

        var inline = images[2];
        Assert.True(inline.IsInline);
        Assert.Null(inline.Reference);
        Assert.Equal(new PdfRectangle(500, 600, 505, 605), inline.Bounds); // after the translation of the form
        Assert.Equal(PdfNames.DeviceGray, inline.ColorSpace);
        Assert.Equal(8, inline.BitsPerComponent);
        using var inlineImage = inline.Decode()!;
        Assert.Equal(128, inlineImage.Frames[0][0, 0].R);
    }

    [Fact]
    public void Example_Underlay()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(200, 200));
        page.CreateContentBuilder().Scale(2, 2).SetFillRgb(1, 0, 0).Rectangle(0, 0, 50, 50).Fill();

        #region Pdf_Underlay
        // A background: the content of the page is painted over it
        page.CreateContentBuilder(PdfContentPlacement.Under)
            .SetFillRgb(0.9, 0.9, 1)
            .Rectangle(page.MediaBox)
            .Fill()
            .Complete();
        #endregion

        using var image = PdfDocument.Open(document.ToByteArray()).Pages[0].Render(new PdfRenderOptions { UseSystemFonts = false, Dpi = 72 });
        AssertColor(Pixel(image, 50, 50), 255, 0, 0);
        AssertColor(Pixel(image, 150, 150), 230, 230, 255);
    }

    [Fact]
    public void CreateContentBuilder_UnderlaysDoNotChangeTheGraphicsStateOfThePage()
    {
        var document = CreateRedactionDocument("1 0 0 rg 0 0 100 100 re f");
        var page = document.Pages[0];

        // The first underlay is painted over the second one, and both under the page; the overlay is painted over all
        page.CreateContentBuilder(PdfContentPlacement.Under).Scale(3, 3).SetFillRgb(0, 1, 0).Rectangle(0, 0, 50, 50).Fill().Complete();
        page.CreateContentBuilder(PdfContentPlacement.Under).SetFillRgb(0, 0, 1).Rectangle(0, 0, 200, 200).Fill().Complete();
        page.CreateContentBuilder().SetFillGray(0).Rectangle(180, 180, 20, 20).Fill().Complete();

        using var image = PdfDocument.Open(document.ToByteArray()).Pages[0].Render(new PdfRenderOptions { UseSystemFonts = false, Dpi = 72 });
        AssertColor(Pixel(image, 50, 50), 255, 0, 0);
        AssertColor(Pixel(image, 120, 120), 0, 255, 0);
        AssertColor(Pixel(image, 170, 170), 0, 0, 255);
        AssertColor(Pixel(image, 190, 190), 0, 0, 0);
    }

    [Fact]
    public void CreateContentBuilder_RejectsAnUnknownPlacement()
    {
        var page = new PdfDocument().Pages.Add(PdfSize.A4);

        Assert.Throws<ArgumentOutOfRangeException>("placement", () => page.CreateContentBuilder((PdfContentPlacement)2));
    }

    [Fact]
    public void Example_Watermark()
    {
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4);
        document.Pages.Add(new PdfSize(500, 500));

        #region Pdf_Watermark
        // On every page, along its diagonal; the text is drawn once and painted by each page
        document.AddWatermark("DRAFT", new PdfWatermarkOptions { Opacity = 0.2 });
        #endregion

        var read = PdfDocument.Open(document.ToByteArray());
        foreach (var page in read.Pages)
        {
            var size = page.DisplaySize;
            var line = Assert.Single(page.ExtractTextLayout().Lines);
            Assert.Equal("DRAFT", line.Text);
            Assert.Equal(Math.Atan2(size.Height, size.Width) * 180 / Math.PI, line.Glyphs[0].Angle, 0.01);
            // Centered on its capital letters: the box of the line, with the ascent and the descent, is a little off center
            Assert.Equal(size.Width / 2, (line.Bounds.Left + line.Bounds.Right) / 2, 20.0);
            Assert.Equal(size.Height / 2, (line.Bounds.Bottom + line.Bounds.Top) / 2, 20.0);
            Assert.True(line.Bounds.Left >= 0 && line.Bounds.Right <= size.Width && line.Bounds.Bottom >= 0 && line.Bounds.Top <= size.Height);
            Assert.Contains("/Artifact", Encoding.Latin1.GetString(page.GetContentData()));
        }

        Assert.Equal(read.Pages[0].Resources!.Get<PdfDictionary>(PdfNames.XObject)!.First().Value, read.Pages[1].Resources!.Get<PdfDictionary>(PdfNames.XObject)!.First().Value);
    }

    [Fact]
    public void AddWatermark_FollowsTheRotationOfThePage()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(200, 400));
        page.Rotation = 90;

        page.AddWatermark("TOP SECRET", new PdfWatermarkOptions { Rotation = 0, FontSize = 20, Placement = PdfContentPlacement.Under });

        // Horizontal on the display: turned by 90 degrees in user space, centered on the page
        var glyph = PdfDocument.Open(document.ToByteArray()).Pages[0].ExtractTextLayout().Lines[0].Glyphs[0];
        Assert.Equal(90, glyph.Angle, 0.01);
        Assert.Equal(20, glyph.FontSize, 0.01);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void AddWatermark_RejectsAnInvalidOpacity(double opacity)
    {
        var page = new PdfDocument().Pages.Add(PdfSize.A4);

        Assert.Throws<ArgumentOutOfRangeException>(() => page.AddWatermark("DRAFT", new PdfWatermarkOptions { Opacity = opacity }));
        Assert.Throws<ArgumentException>(() => page.AddWatermark(""));
    }

    /// <summary>A one-page document of 200 x 200 points with Helvetica as /F1, the content, and other objects from 6.</summary>
    private static PdfDocument CreateRedactionDocument(string content, string resources = "", params string[] objects)
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /Font << /F1 4 0 R >> " + resources + " >> /Contents 5 0 R >>")
            .Add(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>")
            .Add(5, HandWrittenPdf.Stream(content));
        for (var i = 0; i < objects.Length; i++)
        {
            file.Add(6 + i, objects[i]);
        }

        return PdfDocument.Open(file.ToArray());
    }

    private static PdfTextGlyph FindGlyph(PdfPage page, string text) => page.ExtractTextLayout().Lines.SelectMany(line => line.Glyphs).First(glyph => glyph.Text == text);

    [Fact]
    public void Example_Redact()
    {
        #region Pdf_Redact
        var document = new PdfDocument();
        var font = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);
        var page = document.Pages.Add(PdfSize.A4);
        page.CreateContentBuilder()
            .DrawText("Name: John Smith", font, 12, 72, 720);

        // Removes the content under the rectangle and paints it black; the document can then only be saved as a whole
        var result = page.Redact([new PdfRectangle(108, 715, 200, 732)]);
        var redacted = PdfDocument.Open(document.ToByteArray());

        Assert.Equal("Name:", redacted.Pages[0].ExtractText().Trim());
        #endregion

        Assert.Equal(11, result.RemovedGlyphCount);
    }

    [Fact]
    public void Example_RedactWords()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(PdfSize.A4);
        page.CreateContentBuilder().DrawText("Card number: 4111 1111 1111 1111", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 12, 72, 720);

        #region Pdf_RedactWords
        // The boxes of the words to remove, found in the text of the page
        var areas = page.ExtractTextLayout().Lines
            .SelectMany(line => line.Words)
            .Where(word => word.Text.All(char.IsAsciiDigit))
            .Select(word => word.Bounds);
        page.Redact(areas);
        #endregion

        Assert.Equal("Card number:", PdfDocument.Open(document.ToByteArray()).Pages[0].ExtractText().Trim());
    }

    [Fact]
    public void Redact_KeepsTheOtherGlyphsInPlace()
    {
        var document = CreateRedactionDocument("BT /F1 12 Tf 2 Tc 110 Tz 10 100 Td [(AB) -250 (CD)] TJ (EF) Tj ET");
        var page = document.Pages[0];
        var before = FindGlyph(page, "E").Origin;
        var d = FindGlyph(page, "D").Origin;
        var c = FindGlyph(page, "C");

        // C only
        var result = page.Redact([new PdfRectangle(c.Bounds.Left + 1, 90, c.Bounds.Right - 1, 120)], new PdfRedactionOptions { FillColor = null });

        Assert.Equal(1, result.RemovedGlyphCount);
        var read = PdfDocument.Open(document.ToByteArray()).Pages[0];
        Assert.Equal("ABDEF", read.ExtractText().Replace(" ", "", StringComparison.Ordinal));
        Assert.Equal(before.X, FindGlyph(read, "E").Origin.X, 0.001);
        Assert.Equal(d.X, FindGlyph(read, "D").Origin.X, 0.001);
    }

    [Fact]
    public void Redact_RemovesInvisibleTextAndTheLinesOfApostropheOperators()
    {
        var document = CreateRedactionDocument("BT /F1 10 Tf 14 TL 3 Tr 10 150 Td (secret) Tj 0 Tr (top) ' 1 0 (bottom) \" ET");
        var page = document.Pages[0];

        // The first two lines: the invisible text and "top"
        var result = page.Redact([new PdfRectangle(0, 130, 200, 170)], new PdfRedactionOptions { FillColor = null });

        Assert.Equal(9, result.RemovedGlyphCount);
        var read = PdfDocument.Open(document.ToByteArray()).Pages[0];
        Assert.Equal("bottom", read.ExtractText().Trim());
        Assert.Equal(150 - 28, FindGlyph(read, "b").Origin.Y, 0.001);
        Assert.DoesNotContain("secret", Encoding.Latin1.GetString(read.GetContentData()));
    }

    [Fact]
    public void Redact_RedactsACopyOfAFormPaintedOnSeveralPages()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R 7 0 R] /Count 2 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /XObject << /Fm1 6 0 R >> >> /Contents 5 0 R >>")
            .Add(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>")
            .Add(5, HandWrittenPdf.Stream("q 1 0 0 1 10 10 cm /Fm1 Do Q"))
            .Add(6, HandWrittenPdf.Stream("BT /F1 10 Tf 0 0 Td (Left) Tj 100 0 Td (Right) Tj ET", " /Type /XObject /Subtype /Form /BBox [0 0 200 20] /Resources << /Font << /F1 4 0 R >> >>"))
            .Add(7, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Resources << /XObject << /Fm1 6 0 R >> >> /Contents 5 0 R >>");
        var document = PdfDocument.Open(file.ToArray());

        var result = document.Pages[0].Redact([new PdfRectangle(100, 0, 200, 50)]);

        Assert.Equal(5, result.RemovedGlyphCount);
        var read = PdfDocument.Open(document.ToByteArray());
        Assert.Equal("Left", read.Pages[0].ExtractText().Trim());
        Assert.Equal("Left Right", read.Pages[1].ExtractText().Trim());
        Assert.False(read.Pages[0].Resources!.Get<PdfDictionary>(PdfNames.XObject)!.ContainsKey(PdfName.Create("Fm1")));
    }

    [Fact]
    public void Redact_AFormUsesTheTextStateOfTheContentThatPaintsIt()
    {
        // The font is set by the page, before the form shows its text
        var document = CreateRedactionDocument(
            "BT /F1 10 Tf ET /Fm1 Do",
            "/XObject << /Fm1 6 0 R >>",
            HandWrittenPdf.Stream("BT 10 10 Td (Left) Tj 100 0 Td (Right) Tj ET", " /Type /XObject /Subtype /Form /BBox [0 0 200 200] /Resources << >>"));

        var result = document.Pages[0].Redact([new PdfRectangle(100, 0, 200, 50)]);

        Assert.Equal(5, result.RemovedGlyphCount);
        Assert.Equal("Left", PdfDocument.Open(document.ToByteArray()).Pages[0].ExtractText().Trim());
    }

    [Fact]
    public void Redact_KeepsTheResourcesThatAFormWithoutResourcesUses()
    {
        // The form has no resources: it paints the image of the page, outside the area
        var document = CreateRedactionDocument(
            "q 50 0 0 50 0 0 cm /Im1 Do Q q 1 0 0 1 120 120 cm /Fm1 Do Q",
            "/XObject << /Im1 6 0 R /Fm1 7 0 R >>",
            HandWrittenPdf.Stream("FF0000", " /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /ASCIIHexDecode"),
            HandWrittenPdf.Stream("q 50 0 0 50 0 0 cm /Im1 Do Q", " /Type /XObject /Subtype /Form /BBox [0 0 50 50]"));

        var result = document.Pages[0].Redact([new PdfRectangle(0, 0, 100, 100)], new PdfRedactionOptions { FillColor = null });

        Assert.Equal(1, result.RedactedImageCount);
        var images = PdfDocument.Open(document.ToByteArray()).Pages[0].GetImages();
        Assert.Equal(new PdfRectangle(120, 120, 170, 170), Assert.Single(images).Bounds);
    }

    [Fact]
    public void Redact_RemovesTheImagesWithoutPixelsLeftFromTheResources()
    {
        var document = CreateRedactionDocument(
            "q 50 0 0 50 0 0 cm /Im1 Do Q",
            "/XObject << /Im1 6 0 R >>",
            HandWrittenPdf.Stream("FF0000", " /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /ASCIIHexDecode"));

        document.Pages[0].Redact([new PdfRectangle(0, 0, 100, 100)]);

        var read = PdfDocument.Open(document.ToByteArray());
        Assert.Empty(read.Pages[0].GetImages());
        Assert.False(read.Pages[0].Resources!.Get<PdfDictionary>(PdfNames.XObject)!.ContainsKey(PdfName.Create("Im1")));
    }

    [Fact]
    public void Redact_RemovesThePathsInsideTheAreasAndClipsThePathsThatCrossThem()
    {
        // A red rectangle inside the area, a blue one across it
        var document = CreateRedactionDocument("1 0 0 rg 20 20 20 20 re f 0 0 1 rg 10 100 180 40 re f");
        var page = document.Pages[0];

        var result = page.Redact([new PdfRectangle(0, 0, 100, 150)], new PdfRedactionOptions { FillColor = null });

        Assert.Equal(1, result.RemovedPathCount);
        Assert.Equal(1, result.ClippedPathCount);
        var read = PdfDocument.Open(document.ToByteArray()).Pages[0];
        Assert.DoesNotContain("20 20 20 20 re", Encoding.Latin1.GetString(read.GetContentData()));
        using var image = read.Render(new PdfRenderOptions { UseSystemFonts = false, Dpi = 72 });
        AssertColor(Pixel(image, 30, 30), 255, 255, 255);
        AssertColor(Pixel(image, 50, 120), 255, 255, 255);
        AssertColor(Pixel(image, 150, 120), 0, 0, 255);
    }

    [Fact]
    public void Redact_ErasesThePixelsOfTheImagesUnderTheAreas()
    {
        using var pixels = new Image<Rgba32>(10, 10, new Rgba32(255, 0, 0, 255));
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(200, 200));
        page.CreateContentBuilder().DrawImage(PdfImage.FromImage(pixels), 0, 0, 100, 100);
        page.CreateContentBuilder().AppendRawContent("q 50 0 0 50 120 120 cm BI /W 1 /H 1 /BPC 8 /CS /G /F /AHx ID 00> EI Q"u8);

        // The right half of the image, and the whole inline image
        var result = page.Redact([new PdfRectangle(50, 0, 200, 200)], new PdfRedactionOptions { FillColor = null });

        Assert.Equal(2, result.RedactedImageCount);
        var read = PdfDocument.Open(document.ToByteArray()).Pages[0];
        var images = read.GetImages();
        Assert.HasCount(1, images);
        using var image = read.Render(new PdfRenderOptions { UseSystemFonts = false, Dpi = 72 });
        AssertColor(Pixel(image, 25, 50), 255, 0, 0);
        AssertColor(Pixel(image, 75, 50), 255, 255, 255);
        AssertColor(Pixel(image, 140, 140), 255, 255, 255);
    }

    [Fact]
    public void Redact_RemovesTheAnnotationsAndTheFieldsUnderTheAreas()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(200, 200));
        var note = page.Annotations.AddNote(new PdfPoint(20, 20), "secret");
        page.Annotations.AddLink(new PdfRectangle(150, 150, 190, 190), new Uri("https://example.com"));
        var field = document.Form.AddTextField(page, "ssn", new PdfRectangle(10, 50, 90, 70));
        field.Value = "123-45-6789";

        var result = page.Redact([new PdfRectangle(0, 0, 100, 100)]);

        Assert.Equal(2, result.RemovedAnnotationCount);
        var bytes = document.ToByteArray(new PdfSaveOptions { CompressStreams = false });
        var text = Encoding.Latin1.GetString(bytes);
        Assert.DoesNotContain("secret", text);
        Assert.DoesNotContain("123-45-6789", text);
        var read = PdfDocument.Open(bytes);
        Assert.HasCount(1, read.Pages[0].Annotations);
        Assert.Empty(read.Form.GetFields());
        Assert.NotNull(note);
    }

    [Fact]
    public void Redact_RemovesTheReplacementTextOfTheSequencesThatLoseContent()
    {
        var document = CreateRedactionDocument(
            "/Span << /ActualText (hidden) >> BDC BT /F1 10 Tf 10 10 Td (x) Tj ET EMC /Span /P1 BDC BT /F1 10 Tf 10 100 Td (y) Tj ET EMC /Span /P2 BDC BT /F1 10 Tf 150 150 Td (z) Tj ET EMC",
            "/Properties << /P1 << /ActualText (also hidden) >> /P2 << /ActualText (kept) >> >>");
        var page = document.Pages[0];

        page.Redact([new PdfRectangle(0, 0, 100, 120)]);

        var read = PdfDocument.Open(document.ToByteArray(new PdfSaveOptions { CompressStreams = false }));
        Assert.Equal("kept", read.Pages[0].ExtractText().Trim());
        var properties = read.Pages[0].Resources!.Get<PdfDictionary>(PdfNames.Properties)!;
        Assert.False(properties.ContainsKey(PdfName.Create("P1")));
        Assert.True(properties.ContainsKey(PdfName.Create("P2")));
        Assert.DoesNotContain("hidden", Encoding.Latin1.GetString(read.Pages[0].GetContentData()));
    }

    [Fact]
    public void Redact_PaintsTheAreasWithTheFillColor()
    {
        var document = CreateRedactionDocument("BT /F1 10 Tf 10 10 Td (x) Tj ET");

        document.Pages[0].Redact([new PdfRectangle(100, 100, 150, 150)], new PdfRedactionOptions { FillColor = PdfColor.FromRgb(0, 1, 0) });

        using var image = PdfDocument.Open(document.ToByteArray()).Pages[0].Render(new PdfRenderOptions { UseSystemFonts = false, Dpi = 72 });
        AssertColor(Pixel(image, 125, 125), 0, 255, 0);
        AssertColor(Pixel(image, 175, 175), 255, 255, 255);
    }

    [Fact]
    public void Redact_ARedactedDocumentCannotBeSavedIncrementally()
    {
        var document = CreateRedactionDocument("BT /F1 10 Tf 10 10 Td (x) Tj ET");

        document.Pages[0].Redact([new PdfRectangle(0, 0, 50, 50)]);

        Assert.Throws<InvalidOperationException>(() => document.SaveIncremental(new MemoryStream()));
    }

    [Fact]
    public void Redact_DoesNotChangeThePageWhenAContentStreamCannotBeDecoded()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R >>")
            .Add(4, HandWrittenPdf.Stream("not flate data", " /Filter /FlateDecode"));
        var document = PdfDocument.Open(file.ToArray());
        var contents = document.Pages[0].Dictionary[PdfNames.Contents];

        Assert.Throws<InvalidPdfContentException>(() => document.Pages[0].Redact([new PdfRectangle(0, 0, 50, 50)]));

        Assert.Same(contents, document.Pages[0].Dictionary[PdfNames.Contents]);
    }

    private static Rgba32 Pixel(Image<Rgba32> image, int x, int y) => image.Frames[0][x, image.Height - 1 - y];

    private static void AssertColor(Rgba32 pixel, int r, int g, int b, int tolerance = 2)
    {
        Assert.True(Math.Abs(pixel.R - r) <= tolerance && Math.Abs(pixel.G - g) <= tolerance && Math.Abs(pixel.B - b) <= tolerance, $"The pixel is ({pixel.R}, {pixel.G}, {pixel.B}), not ({r}, {g}, {b})");
    }
}
