using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.WebPlatform.Imaging.Tests;

/// <summary>The float areas of the shapes from images (<c>shape-outside: &lt;image&gt;</c>): the alpha channel of the image rasterized by the backend.</summary>
public sealed class ShapeImageTests : IDisposable
{
    private readonly TestPages _pages = new();

    public void Dispose() => _pages.Dispose();

    private static double GetLeft(Document document, string id) => document.GetElementById(id)!.GetBoundingClientRect().Left;

    [Fact]
    public void Gradient()
    {
        // The opaque half of the gradient is the float area; a vertical gradient shortens the lines of its opaque half only
        var document = _pages.Load("""
            <div style='width: 200px'>
              <div style='float: left; width: 100px; height: 20px; shape-outside: linear-gradient(to right, black 50%, transparent 50%)'></div>
              <span id=a>X</span>
            </div>
            <div style='width: 200px; clear: both'>
              <div style='float: left; width: 100px; height: 20px; shape-outside: linear-gradient(to bottom, black 50%, transparent 50%)'></div>
              <span id=b>X</span><br><span id=c>X</span>
            </div>
            """).Document;
        Assert.Equal(50, GetLeft(document, "a"));
        Assert.Equal(100, GetLeft(document, "b"));
        Assert.Equal(0, GetLeft(document, "c"));
    }

    [Theory]
    [InlineData("", 100)]
    [InlineData("shape-image-threshold: 0.5", 50)]
    [InlineData("shape-image-threshold: 0.5; shape-margin: 10px", 60)]
    [InlineData("shape-image-threshold: 1", 0)]
    public void Image_ThresholdAndMargin(string style, double expected)
    {
        // An image of 2x1 pixels stretched to the content box, sampled without interpolation: the left pixel is opaque, the alpha
        // of the right one is 0.4
        _pages.Resources["/shape.png"] = (TestPages.CreatePng(2, 1, new Rgba32(0, 0, 0, 255), new Rgba32(0, 0, 0, 102)), "image/png");
        var document = _pages.Load($"""
            <div style='width: 200px'>
              <div style='float: left; width: 100px; height: 20px; shape-outside: url(/shape.png); {style}'></div>
              <span id=a>X</span>
            </div>
            """).Document;
        Assert.Equal(expected, GetLeft(document, "a"));
    }

    [Fact]
    public void VerticalWritingMode()
    {
        // The lines of a vertical block are shortened by the extent of the shape along the vertical axis
        var document = _pages.Load("""
            <div style='writing-mode: vertical-rl; height: 100px'>
              <div style='float: left; width: 20px; height: 60px; shape-outside: linear-gradient(to bottom, black 50%, transparent 50%)'></div>
              <span id=a>X</span>
            </div>
            """).Document;
        Assert.Equal(30, document.GetElementById("a")!.GetBoundingClientRect().Top);
    }
}
