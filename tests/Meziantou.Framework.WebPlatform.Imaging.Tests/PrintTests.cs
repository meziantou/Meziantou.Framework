using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.WebPlatform.Imaging.Tests;

/// <summary>The printing of the documents into page images (<see cref="ImagingPrintExtensions"/>).</summary>
public sealed class PrintTests : IDisposable
{
    private readonly TestPages _pages = new();

    public void Dispose() => _pages.Dispose();

    [Fact]
    public void RenderPages_OneImagePerPage()
    {
        // Two pages of 100x80 with margins of 10px: the first page area is green, the second one starts with a blue box; the
        // background of the page box (@page) is yellow, the margins of the second page are 20px
        var context = _pages.Load("""
            <style>
              @page { size: 100px 80px; margin: 10px; background: yellow }
              @page :left { margin: 20px }
              #a { height: 60px; background: red }
              @media print { #a { background: lime } }
            </style>
            <div id=a></div>
            <div style='height: 10px; background: blue'></div>
            """);

        var pages = context.RenderPages();
        try
        {
            Assert.Equal(2, pages.Count);
            Assert.Equal((100, 80), (pages[0].Width, pages[0].Height));
            Assert.Equal(Colors.Yellow, pages[0].GetPixel(5, 5));
            Assert.Equal(Colors.Lime, pages[0].GetPixel(50, 40));
            Assert.Equal(Colors.Blue, pages[1].GetPixel(25, 25));

            // The background of the page box covers the page, its margins included (the canvas of the document is transparent)
            Assert.Equal(Colors.Yellow, pages[1].GetPixel(25, 35));
            Assert.Equal(Colors.Yellow, pages[1].GetPixel(15, 15));
        }
        finally
        {
            foreach (var page in pages)
            {
                page.Dispose();
            }
        }

        // The screen is not changed by the printing
        using var screenshot = context.TakeScreenshot();
        Assert.Equal(Colors.Red, screenshot.GetPixel(5, 5));
    }

    [Fact]
    public void RenderPages_OmitBackground_TheSheetIsTransparent()
    {
        var context = _pages.Load("<style>@page { size: 100px 80px; margin: 10px }</style><div style='height: 20px; background: blue'></div>");

        var pages = context.RenderPages(options: new ScreenshotOptions { OmitBackground = true });
        try
        {
            Assert.Single(pages);
            Assert.Equal(new Rgba32(0, 0, 0, 0), pages[0].GetPixel(5, 5));
            Assert.Equal(new Rgba32(0, 0, 0, 0), pages[0].GetPixel(50, 50));
            Assert.Equal(Colors.Blue, pages[0].GetPixel(50, 20));
        }
        finally
        {
            foreach (var page in pages)
            {
                page.Dispose();
            }
        }
    }

    [Fact]
    public void RenderPages_Landscape()
    {
        var context = _pages.Load("<p>a</p>");
        var pages = context.RenderPages(new PrintOptions { PageWidth = 100, PageHeight = 80, Landscape = true });
        try
        {
            Assert.Equal((80, 100), (pages[0].Width, pages[0].Height));
        }
        finally
        {
            DisposeAll(pages);
        }
    }

    [Fact]
    public void RenderPages_Scale_ScalesTheContentOfThePageArea()
    {
        // A page area of 80x60 at the scale 0.5 lays the content out in 160x120 and paints it at half its size; the margins are not scaled
        var context = _pages.Load("<style>@page { size: 100px 80px; margin: 10px }</style><div style='height: 20px; background: blue'></div><div style='height: 200px'></div>");
        var pages = context.RenderPages(new PrintOptions { Scale = 0.5 });
        try
        {
            Assert.Equal(2, pages.Count);
            Assert.Equal(Colors.White, pages[0].GetPixel(5, 5));
            Assert.Equal(Colors.Blue, pages[0].GetPixel(11, 11));
            Assert.Equal(Colors.Blue, pages[0].GetPixel(88, 19));
            Assert.Equal(Colors.White, pages[0].GetPixel(88, 21));
        }
        finally
        {
            DisposeAll(pages);
        }

        Assert.Throws<ArgumentException>("options", () => context.RenderPages(new PrintOptions { Scale = 3 }));
    }

    [Fact]
    public void RenderPages_PreferCssPageSize_False_CentersThePageOnThePaper()
    {
        var context = _pages.Load("<style>@page { size: 50px 40px; margin: 0 } html { background: blue }</style>");
        var pages = context.RenderPages(new PrintOptions { PageWidth = 100, PageHeight = 80, PreferCssPageSize = false });
        try
        {
            Assert.Equal((100, 80), (pages[0].Width, pages[0].Height));
            Assert.Equal(Colors.White, pages[0].GetPixel(10, 10));
            Assert.Equal(Colors.Blue, pages[0].GetPixel(26, 21));
            Assert.Equal(Colors.Blue, pages[0].GetPixel(74, 59));
            Assert.Equal(Colors.White, pages[0].GetPixel(76, 61));
        }
        finally
        {
            DisposeAll(pages);
        }

        // A larger page is scaled down to fit, and a landscape page box is printed on a landscape paper
        context = _pages.Load("<style>@page { size: 400px 200px; margin: 0 } html { background: blue }</style>");
        pages = context.RenderPages(new PrintOptions { PageWidth = 80, PageHeight = 100, PreferCssPageSize = false });
        try
        {
            Assert.Equal((100, 80), (pages[0].Width, pages[0].Height));
            Assert.Equal(Colors.White, pages[0].GetPixel(50, 10));
            Assert.Equal(Colors.Blue, pages[0].GetPixel(50, 40));
        }
        finally
        {
            DisposeAll(pages);
        }
    }

    [Theory]
    [InlineData("2", new[] { 2 })]
    [InlineData("2-3, 5", new[] { 2, 3, 5 })]
    [InlineData("-2,4-", new[] { 1, 2, 4, 5 })]
    [InlineData("5, 1, 1, 9-12", new[] { 1, 5 })]
    public void RenderPages_PageRanges(string ranges, int[] expected)
    {
        // Five pages whose background shows their number (counter(page) counts all the pages)
        var context = _pages.Load("""
            <style>
              @page { size: 50px 50px; margin: 0 }
              div { height: 50px; break-after: page }
            </style>
            <div style='background: rgb(1, 0, 0)'></div><div style='background: rgb(2, 0, 0)'></div><div style='background: rgb(3, 0, 0)'></div><div style='background: rgb(4, 0, 0)'></div><div style='background: rgb(5, 0, 0)'></div>
            """);
        var pages = context.RenderPages(new PrintOptions { PageRanges = ranges });
        try
        {
            Assert.Equal(expected, pages.Select(page => (int)page.GetPixel(25, 25).R));
        }
        finally
        {
            DisposeAll(pages);
        }
    }

    [Theory]
    [InlineData("a")]
    [InlineData("0")]
    [InlineData("3-2")]
    [InlineData("1-2-3")]
    public void RenderPages_PageRanges_Invalid(string ranges)
    {
        var context = _pages.Load("<p>a</p>");
        Assert.Throws<ArgumentException>("value", () => context.RenderPages(new PrintOptions { PageRanges = ranges }));
    }

    [Fact]
    public void RenderPages_PageRanges_NoPage()
    {
        var context = _pages.Load("<p>a</p>");
        Assert.Throws<InvalidOperationException>(() => context.RenderPages(new PrintOptions { PageRanges = "4-" }));
    }

    [Fact]
    public void RenderPages_PrintBackground_False()
    {
        // The backgrounds are white, except the ones of print-color-adjust: exact; the text close to white is darkened
        var context = _pages.Load("""
            <style>
              @page { size: 100px 80px; margin: 0 }
              body { background: yellow }
              #a { height: 20px; background: blue; color: white }
              #b { height: 20px; background: red; print-color-adjust: exact }
            </style>
            <div id=a>x</div><div id=b></div><div id=c style='height: 20px'></div>
            """);
        var pages = context.RenderPages(new PrintOptions { PrintBackground = false });
        try
        {
            Assert.Equal(Colors.White, pages[0].GetPixel(50, 10));
            Assert.Equal(new Rgba32(0xAB, 0xAB, 0xAB), pages[0].GetPixel(5, 5));
            Assert.Equal(Colors.Red, pages[0].GetPixel(50, 30));
            Assert.Equal(Colors.White, pages[0].GetPixel(50, 50));
        }
        finally
        {
            DisposeAll(pages);
        }
    }

    private static void DisposeAll(IReadOnlyList<Image<Rgba32>> pages)
    {
        foreach (var page in pages)
        {
            page.Dispose();
        }
    }
}
