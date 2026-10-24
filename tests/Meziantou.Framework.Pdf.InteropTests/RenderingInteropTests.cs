using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf.TestHarness.ExternalTools;

namespace Meziantou.Framework.Pdf.InteropTests;

/// <summary>Poppler and MuPDF render the pages the library writes: the colors at points of each page are those the content paints.</summary>
public sealed class RenderingInteropTests
{
    public static TheoryData<string, int> AdvancedGraphicsPages
    {
        get
        {
            var data = new TheoryData<string, int>();
            foreach (var renderer in new[] { "pdftoppm", "mutool" })
            {
                for (var page = 0; page < InteropDocuments.AdvancedGraphicsPages.Length; page++)
                {
                    data.Add(renderer, page);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AdvancedGraphicsPages))]
    public async Task AdvancedGraphicsAreRendered(string renderer, int page)
    {
        var path = InteropDocuments.Write("advanced-graphics", objectStreams: true);
        var output = Path.ChangeExtension(path, null) + "-" + renderer + "-" + page.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string png;
        if (renderer == "mutool")
        {
            var mutool = await MutoolTool.LocateAsync(XunitCancellationToken);
            png = await mutool.RenderPageAsync(path, page + 1, 72, output + ".png", XunitCancellationToken);
        }
        else
        {
            var poppler = await PopplerTool.LocateAsync(XunitCancellationToken);
            png = await poppler.RenderPageAsync(path, page + 1, 72, output, XunitCancellationToken);
        }

        using var image = Image.Load<Rgb24>(png);
        var frame = image.Frames[0];
        var (name, samples) = InteropDocuments.AdvancedGraphicsPages[page];
        Assert.Equal(200, frame.Width);
        foreach (var (x, y, r, g, b, tolerance) in samples)
        {
            // The y axis of the page goes up, the rows of the image go down
            var pixel = frame[(int)x, (int)(200 - y)];
            var message = $"{name}: the pixel at ({x}, {y}) is ({pixel.R}, {pixel.G}, {pixel.B}), not ({r}, {g}, {b})";
            Assert.True(Math.Abs(pixel.R - r) <= tolerance && Math.Abs(pixel.G - g) <= tolerance && Math.Abs(pixel.B - b) <= tolerance, message);
        }
    }
}
