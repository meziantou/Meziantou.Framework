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
}
