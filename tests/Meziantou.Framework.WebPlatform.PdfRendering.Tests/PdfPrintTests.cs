using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.TestHarness;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Imaging;
using Meziantou.Framework.WebPlatform.Imaging.Tests;
using PdfRenderer = Meziantou.Framework.Pdf.Rendering.PdfRenderer;
using PdfRenderOptions = Meziantou.Framework.Pdf.Rendering.PdfRenderOptions;

namespace Meziantou.Framework.WebPlatform.PdfRendering.Tests;

/// <summary>The printing of the documents into PDF documents (<see cref="PdfPrintExtensions"/>).</summary>
public sealed class PdfPrintTests : IDisposable
{
    private const string PageStyle = "<style>@page { size: 200px 150px; margin: 0 }</style>";

    private readonly TestPages _pages = new();

    public void Dispose() => _pages.Dispose();

    public static TheoryData<string> RenderingCases => [.. RenderingDocuments.Keys];

    private static readonly Dictionary<string, string> RenderingDocuments = new(StringComparer.Ordinal)
    {
        ["backgrounds"] = """
            <div style='position: absolute; left: 10px; top: 10px; width: 60px; height: 40px; background: #3366cc'></div>
            <div style='position: absolute; left: 80px; top: 10px; width: 50px; height: 30px; border: 5px solid #cc3333; background: #ffcc00'></div>
            <div style='position: absolute; left: 10px; top: 70px; width: 60px; height: 40px; border-radius: 12px; background: #33aa55'></div>
            <div style='position: absolute; left: 90px; top: 70px; width: 60px; height: 40px; border: 4px dashed #000; border-radius: 8px'></div>
            """,
        ["gradients"] = """
            <div style='position: absolute; left: 10px; top: 10px; width: 80px; height: 50px; background: linear-gradient(90deg, red, blue)'></div>
            <div style='position: absolute; left: 100px; top: 10px; width: 80px; height: 50px; background: radial-gradient(circle, yellow, green)'></div>
            <div style='position: absolute; left: 10px; top: 80px; width: 80px; height: 50px; background: repeating-linear-gradient(45deg, #000 0 5px, #fff 5px 10px)'></div>
            <div style='position: absolute; left: 100px; top: 80px; width: 80px; height: 50px; background: #f0f; '><div style='height: 100%; background: linear-gradient(transparent, #00f)'></div></div>
            """,
        ["opacity"] = """
            <div style='position: absolute; left: 10px; top: 10px; width: 80px; height: 80px; background: blue; opacity: 0.5'>
              <div style='margin: 20px; height: 40px; background: red'></div>
            </div>
            <div style='position: absolute; left: 100px; top: 10px; width: 80px; height: 80px; background: #ffcc00'>
              <div style='margin: 20px; height: 40px; background: #3399ff; mix-blend-mode: multiply'></div>
            </div>
            <div style='position: absolute; left: 10px; top: 100px; width: 80px; height: 30px; background: rgba(0, 128, 0, 0.4)'></div>
            """,
        ["transforms"] = """
            <div style='position: absolute; left: 30px; top: 30px; width: 60px; height: 40px; background: #3366cc; transform: rotate(30deg)'></div>
            <div style='position: absolute; left: 120px; top: 50px; width: 40px; height: 40px; background: #cc3333; transform: scale(1.5, 0.5)'></div>
            """,
        ["text"] = """
            <div style='position: absolute; left: 10px; top: 10px; font: 20px/1 Ahem; color: #3366cc'>Hello</div>
            <div style='position: absolute; left: 10px; top: 50px; font: 10px/1 Ahem; color: red; text-decoration: underline'>Underlined text</div>
            <div style='position: absolute; left: 10px; top: 80px; font: 16px/1 Ahem; color: rgba(0, 0, 0, 0.5)'>Half</div>
            """,
        ["shadows"] = """
            <div style='position: absolute; left: 30px; top: 30px; width: 80px; height: 50px; background: white; box-shadow: 4px 4px 8px rgba(0, 0, 0, 0.6)'></div>
            <div style='position: absolute; left: 130px; top: 30px; width: 40px; height: 40px; background: #3366cc; box-shadow: 0 0 0 5px #cc3333'></div>
            """,
        ["clips"] = """
            <div style='position: absolute; left: 20px; top: 20px; width: 80px; height: 80px; border-radius: 40px; overflow: hidden; background: #ddd'>
              <div style='width: 40px; height: 120px; background: #cc3333'></div>
            </div>
            <div style='position: absolute; left: 120px; top: 20px; width: 50px; height: 50px; overflow: hidden'>
              <div style='width: 100px; height: 100px; background: #33aa55'></div>
            </div>
            """,
        ["svg"] = """
            <svg style='position: absolute; left: 10px; top: 10px' width='180' height='130'>
              <circle cx='40' cy='40' r='30' fill='#3366cc' />
              <path d='M100 10 L170 60 L100 110 Z' fill='none' stroke='#cc3333' stroke-width='6' stroke-linejoin='round' />
              <rect x='10' y='90' width='60' height='30' fill='#33aa55' fill-opacity='0.5' />
            </svg>
            """,
        ["filters"] = """
            <div style='position: absolute; left: 30px; top: 30px; width: 60px; height: 60px; background: #3366cc; filter: blur(3px)'></div>
            <div style='position: absolute; left: 120px; top: 30px; width: 60px; height: 60px; background: #cc3333; filter: grayscale(1)'></div>
            """,
        ["3d"] = """
            <div style='position: absolute; left: 50px; top: 30px; width: 100px; height: 80px; background: #3366cc; transform: perspective(200px) rotateY(40deg)'></div>
            """,
        // The instances of a variable font (the bundled Arimo at a weight other than its default) are drawn as paths
        ["variable-font"] = """
            <div style='position: absolute; left: 10px; top: 10px; font: bold 24px/1 sans-serif'>Bold text</div>
            <div style='position: absolute; left: 10px; top: 50px; font: 600 18px/1 sans-serif; font-style: italic'>Semi bold</div>
            """,
        ["conic"] = """
            <div style='position: absolute; left: 50px; top: 25px; width: 100px; height: 100px; border-radius: 50%; background: conic-gradient(red, yellow, lime, aqua, blue, magenta, red)'></div>
            """,
    };

    [Fact]
    public void PrintToPdf_Example()
    {
        #region PdfPrintExtensions_PrintToPdf
        // The rendering backend keeps and decodes the images, and rasterizes what PDF cannot draw
        using var context = new BrowsingContext(new BrowsingContextOptions { RenderingBackend = new ImagingRenderingBackend() });
        context.Navigate("https://example.com/", _ => """
            <!DOCTYPE html>
            <html lang="en">
            <title>Monthly report</title>
            <style>@page { size: A4; margin: 2cm }</style>
            <h1>Monthly report</h1>
            <p>See the <a href="https://example.com/details">details</a>.</p>
            """);
        context.EventLoop.RunUntilIdle();

        // A tagged PDF document (PDF/UA-1): text, links, bookmarks and the structure of the document
        var pdf = context.PrintToPdf(options: new PdfPrintOptions { PdfUAConformance = PdfUAConformance.PdfUA1 });
        var bytes = pdf.ToByteArray();
        #endregion

        var reopened = PdfDocument.Open(bytes);
        Assert.Single(reopened.Pages);
        Assert.Equal("Monthly report", reopened.Information.Title);
        Assert.Equal("Monthly report", Assert.Single(reopened.Outlines).Title);
    }

    [Fact]
    public void PrintToPdf_OnePagePerPrintedPage()
    {
        var context = _pages.Load("""
            <style>
              @page { size: 100px 80px; margin: 10px; background: yellow }
              @page :left { margin: 20px }
              #a { height: 60px; background: lime }
            </style>
            <div id=a></div>
            <div style='height: 10px; background: blue'></div>
            """);

        var pdf = Reopen(context.PrintToPdf());

        Assert.Equal(2, pdf.Pages.Count);
        Assert.Equal(75, pdf.Pages[0].MediaBox.Width, 6);
        Assert.Equal(60, pdf.Pages[0].MediaBox.Height, 6);
        AssertRendersAsTheRasterizer(context, pdf);
    }

    [Theory]
    [MemberData(nameof(RenderingCases))]
    public void PrintToPdf_RendersAsTheRasterizer(string name)
    {
        var context = _pages.Load(PageStyle + $"<meta data-case='{name}'>" + RenderingDocuments[name]);

        var pdf = Reopen(context.PrintToPdf());

        AssertRendersAsTheRasterizer(context, pdf);
    }

    [Fact]
    public void PrintToPdf_TextIsEmbeddedAndExtractable()
    {
        var context = _pages.Load(PageStyle + LoadFont() + "<style>p { font: 14px Noto; margin: 0 }</style><p>office hours</p><p>Café — 1€</p>");

        var pdf = Reopen(context.PrintToPdf());
        var page = pdf.Pages[0];
        var fonts = page.Resources!.Get<PdfDictionary>(PdfName.Create("Font"))!;
        var type0 = Assert.IsType<PdfDictionary>(fonts.GetResolved(Assert.Single(fonts.Keys)));

        Assert.Equal("office hours\nCafé — 1€", page.ExtractText());
        Assert.Equal("Type0", type0.GetName(PdfName.Create("Subtype"))!.Value);
        Assert.Matches("^[A-Z]{6}\\+", type0.GetName(PdfName.Create("BaseFont"))!.Value);
    }

    [Fact]
    public void PrintToPdf_ImagesAreEmbedded()
    {
        // A PNG image with transparent pixels (an image and its soft mask), and a JPEG image (embedded as it is)
        var colors = new Rgba32[20 * 20];
        for (var i = 0; i < colors.Length; i++)
        {
            colors[i] = i % 20 < 10 ? new Rgba32(255, 0, 0, 255) : new Rgba32(0, 128, 0, 128);
        }

        using var jpeg = new Image<Rgba32>(20, 20, new Rgba32(0, 160, 220, 255));
        using var jpegData = new MemoryStream();
        jpeg.Save(jpegData, new Meziantou.Framework.Imaging.Formats.JpegEncoder { Quality = 95 });
        _pages.Resources["/a.png"] = (TestPages.CreatePng(20, 20, colors), "image/png");
        _pages.Resources["/b.jpg"] = (jpegData.ToArray(), "image/jpeg");
        var context = _pages.Load(PageStyle + "<img src='/a.png'><img src='/b.jpg'><img src='/b.jpg' width=60 height=30>");

        var pdf = Reopen(context.PrintToPdf());
        var xobjects = pdf.Pages[0].Resources!.Get<PdfDictionary>(PdfName.Create("XObject"))!;
        var images = xobjects.Keys.Select(key => Assert.IsType<PdfStream>(xobjects.GetResolved(key))).ToList();

        // The JPEG image drawn twice is embedded once
        Assert.Equal(2, images.Count);
        Assert.Contains(images, image => image.Dictionary.ContainsKey(PdfName.Create("SMask")));
        Assert.Contains(images, image => image.Dictionary.GetName(PdfName.Create("Filter"))?.Value == "DCTDecode");
        AssertRendersAsTheRasterizer(context, pdf);
    }

    [Fact]
    public void PrintToPdf_LinksAreAnnotations()
    {
        var context = _pages.Load("""
            <style>@page { size: 200px 300px; margin: 0 }</style>
            <p style='margin: 0'><a href='https://example.org/page'>External</a> and <a href='#target'>internal</a></p>
            <p style='margin: 0'><a href='other.html'>Relative</a></p>
            <div style='height: 200px'></div>
            <h2 id=target style='margin: 0; font-size: 10px'>Target</h2>
            """);

        var pdf = Reopen(context.PrintToPdf());
        var links = pdf.Pages[0].Annotations.OfType<PdfLinkAnnotation>().ToList();

        Assert.Equal(3, links.Count);
        Assert.Equal(new Uri("https://example.org/page"), links[0].Uri);
        Assert.Equal("External", links[0].Contents);
        Assert.NotNull(links[1].Destination);
        Assert.Equal(new Uri("https://example.com/other.html"), links[2].Uri);

        // The rectangle of the first link covers its text (Ahem: 8 glyphs of 10px at the top-left of the page)
        var rect = links[0].Rectangle;
        Assert.Equal(0, rect.Left, 1);
        Assert.Equal(80 * 0.75, rect.Right, 1);
        Assert.Equal(300 * 0.75, rect.Top, 1);
    }

    [Fact]
    public void PrintToPdf_PrintOptionsOfPlaywright()
    {
        // Three pages: the second and the third ones are printed, in landscape, scaled; the link to the heading of the third page is
        // kept, the one to the first page (not printed) is not
        var context = _pages.Load("""
            <style>@page { margin: 0 } div { height: 100px; break-after: page }</style>
            <div><h2 id=first style='margin: 0; font-size: 10px'>First</h2></div>
            <div><a href='#first'>first</a> <a href='#third'>third</a></div>
            <div><h2 id=third style='margin: 0; font-size: 10px'>Third</h2></div>
            """);

        var pdf = Reopen(context.PrintToPdf(new PrintOptions { PageWidth = 200, PageHeight = 300, Landscape = true, Scale = 0.5, PageRanges = "2-3" }));

        Assert.Equal(2, pdf.Pages.Count);
        var page = pdf.Pages[0];
        Assert.Equal(300 * 0.75, page.MediaBox.Width, 1);
        Assert.Equal(200 * 0.75, page.MediaBox.Height, 1);
        var link = Assert.Single(page.Annotations.OfType<PdfLinkAnnotation>());
        Assert.Equal("third", link.Contents);
    }

    [Fact]
    public void PrintToPdf_HeadingsAreTheOutline()
    {
        var context = _pages.Load(PageStyle + "<h1>One</h1><h2>One.A</h2><h2>One.B</h2><h1>Two</h1><h3>Two.A.1</h3>");

        var pdf = Reopen(context.PrintToPdf());

        Assert.Equal(["One", "Two"], pdf.Outlines.Select(item => item.Title));
        Assert.Equal(["One.A", "One.B"], pdf.Outlines[0].Children.Select(item => item.Title));
        Assert.Equal(["Two.A.1"], pdf.Outlines[1].Children.Select(item => item.Title));
    }

    [Fact]
    public void PrintToPdf_StructureTreeOfTheElements()
    {
        _pages.Resources["/a.png"] = (TestPages.CreatePng(1, 1, new Rgba32(255, 0, 0, 255)), "image/png");
        var context = _pages.Load("""
            <style>@page { size: 300px 400px; margin: 0 }</style>
            <h1>Title</h1>
            <p>Some <em>text</em> <a href='https://example.org/'>link</a></p>
            <ul><li>First</li><li>Second</li></ul>
            <table><tr><th>H</th><td>D</td></tr></table>
            <img src='/a.png' alt='A red dot' width=10 height=10>
            <img src='/a.png' alt='' width=10 height=10>
            <div aria-hidden=true>Hidden</div>
            """);

        var pdf = Reopen(context.PrintToPdf());
        var root = Assert.Single(pdf.StructureTree.Elements);

        Assert.Equal("Document", root.Type);
        Assert.Equal(["H1", "P", "L", "Table", "Figure"], root.Children.Select(element => element.Type));
        Assert.Equal(["Link"], root.Children[1].Children.Select(element => element.Type));
        Assert.Equal(["LI", "LI"], root.Children[2].Children.Select(element => element.Type));
        Assert.Equal(["LBody"], root.Children[2].Children[0].Children.Select(element => element.Type));
        Assert.Equal(["TH", "TD"], root.Children[3].Children.Single(element => element.Type is "TR" or "TBody").Children.SelectMany(element => element.Type == "TR" ? element.Children : [element]).Select(element => element.Type));
        Assert.Equal("A red dot", root.Children[4].AlternateDescription);

        // The decorative image and the hidden element are artifacts: the text of the page is the text of the structure
        var content = System.Text.Encoding.Latin1.GetString(pdf.Pages[0].GetContentData());
        Assert.Contains("/Artifact", content, StringComparison.Ordinal);
    }

    [Fact]
    public void PrintToPdf_TranslucentGroupIsContent()
    {
        // The group of the paragraph starts with its background (an artifact): the group is marked as its text
        var context = _pages.Load(PageStyle + "<p style='opacity: 0.5; background: yellow'>Faded</p>");

        var pdf = Reopen(context.PrintToPdf());
        var root = Assert.Single(pdf.StructureTree.Elements);

        Assert.Equal(["P"], root.Children.Select(element => element.Type));
        Assert.Equal("Faded", pdf.Pages[0].ExtractText());
    }

    [Fact]
    public void PrintToPdf_Metadata()
    {
        var context = _pages.Load("<html lang=fr><title>The title</title><meta name=author content='An author'><p>Text</p></html>");

        var pdf = Reopen(context.PrintToPdf());

        Assert.Equal("The title", pdf.Information.Title);
        Assert.Equal("An author", pdf.Information.Author);
        Assert.Equal("fr", pdf.Language);
    }

    [Fact]
    public void PrintToPdf_Options()
    {
        var context = _pages.Load("<h1>Heading</h1><a href='https://example.org/'>link</a>");

        var pdf = Reopen(context.PrintToPdf(options: new PdfPrintOptions { Tagged = false, IncludeLinks = false, IncludeOutlines = false, Title = "Custom" }));

        Assert.False(pdf.StructureTree.Exists);
        Assert.Empty(pdf.Pages[0].Annotations);
        Assert.Empty(pdf.Outlines);
        Assert.Equal("Custom", pdf.Information.Title);
        Assert.Throws<ArgumentException>(() => context.PrintToPdf(options: new PdfPrintOptions { RasterizationScale = 0 }));
    }

    [Fact]
    public void PrintToPdf_ToStream()
    {
        var context = _pages.Load(PageStyle + "<p>Text</p>");
        using var stream = new MemoryStream();

        context.PrintToPdf(stream);

        Assert.Equal("%PDF-"u8.ToArray(), stream.ToArray()[..5]);
        Assert.Single(PdfDocument.Open(stream.ToArray()).Pages);
    }

    [Fact]
    public void PrintToPdf_TheOutputIsDeterministic()
    {
        var context = _pages.Load(PageStyle + RenderingDocuments["gradients"] + RenderingDocuments["text"]);

        Assert.Equal(context.PrintToPdf().ToByteArray(), context.PrintToPdf().ToByteArray());
    }

    /// <summary>Serves the Noto Sans font, and returns the style that declares it as the <c>Noto</c> family.</summary>
    private string LoadFont()
    {
        _pages.Resources["/noto.ttf"] = (File.ReadAllBytes(RepositoryPaths.Fonts / "noto-sans/NotoSans-Regular.subset.ttf"), "font/ttf");
        return "<style>@font-face { font-family: Noto; src: url(/noto.ttf) }</style>";
    }

    private static PdfDocument Reopen(PdfDocument document) => PdfDocument.Open(document.ToByteArray());

    /// <summary>Renders the pages of the PDF document and compares them to the pages rasterized from the same document.</summary>
    /// <remarks>
    /// Set the <c>PDF_RENDERING_TEST_OUTPUT</c> environment variable to a directory to save the PDF documents and the images
    /// compared (<c>-expected.png</c>, <c>-actual.png</c>).
    /// </remarks>
    private static void AssertRendersAsTheRasterizer(BrowsingContext context, PdfDocument pdf, [System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var output = Environment.GetEnvironmentVariable("PDF_RENDERING_TEST_OUTPUT");
        var name = testName + (context.Document.QuerySelector("[data-case]")?.GetAttribute("data-case") is { } caseName ? "-" + caseName : "");
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, name + ".pdf"), pdf.ToByteArray());
        }

        var expected = context.RenderPages();
        try
        {
            Assert.Equal(expected.Count, pdf.Pages.Count);
            for (var i = 0; i < expected.Count; i++)
            {
                using var actual = PdfRenderer.RenderPage(pdf.Pages[i], new PdfRenderOptions { Dpi = 96, UseSystemFonts = false });
                if (!string.IsNullOrEmpty(output))
                {
                    expected[i].Save(Path.Combine(output, $"{name}-{i + 1}-expected.png"));
                    actual.Save(Path.Combine(output, $"{name}-{i + 1}-actual.png"));
                }

                AssertSimilar(expected[i], actual, $"page {i + 1}");
            }
        }
        finally
        {
            foreach (var page in expected)
            {
                page.Dispose();
            }
        }
    }

    /// <summary>
    /// Asserts that two images are the same but for the antialiasing of the edges: at most 2% of the pixels differ by more
    /// than 48 in a channel, and the mean difference is small.
    /// </summary>
    private static void AssertSimilar(Image<Rgba32> expected, Image<Rgba32> actual, string name)
    {
        Assert.Equal((expected.Width, expected.Height), (actual.Width, actual.Height));
        var different = 0;
        var total = 0L;
        var expectedFrame = expected.Frames[0];
        var actualFrame = actual.Frames[0];
        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                var a = expectedFrame[x, y];
                var b = actualFrame[x, y];
                var difference = Math.Max(Math.Max(Math.Abs(a.R - b.R), Math.Abs(a.G - b.G)), Math.Abs(a.B - b.B));
                total += difference;
                if (difference > 48)
                {
                    different++;
                }
            }
        }

        var count = expected.Width * expected.Height;
        var mean = (double)total / count;
        Assert.True(different <= count * 0.02 && mean < 4, $"{name}: {different} of {count} pixels differ, mean difference {mean:F2}");
    }
}
