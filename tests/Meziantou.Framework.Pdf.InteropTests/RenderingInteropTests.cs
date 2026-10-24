using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf.TestHarness.ExternalTools;

namespace Meziantou.Framework.Pdf.InteropTests;

/// <summary>Poppler, MuPDF and the renderer of the library render the pages the library writes: the colors at points of each page are those the content paints.</summary>
public sealed class RenderingInteropTests
{
    public static TheoryData<string, int> AdvancedGraphicsPages
    {
        get
        {
            var data = new TheoryData<string, int>();
            foreach (var renderer in new[] { "pdftoppm", "mutool", "library" })
            {
                for (var page = 0; page < InteropDocuments.AdvancedGraphicsPages.Length; page++)
                {
                    data.Add(renderer, page);
                }
            }

            return data;
        }
    }

    // The pages a renderer is known to draw differently from ISO 32000-2: MuPDF knocks out the fill of a path with its
    // stroke in a knockout group (11.7.4.4 composites the stroke over the fill); Poppler does not knock out the elements
    // of an isolated knockout group
    private static readonly HashSet<(string Renderer, string Page)> KnownDeviations =
    [
        ("mutool", "knockout-fill-and-stroke"),
        ("pdftoppm", "knockout-group"),
    ];

    [Theory]
    [MemberData(nameof(AdvancedGraphicsPages))]
    public async Task AdvancedGraphicsAreRendered(string renderer, int page)
    {
        if (KnownDeviations.Contains((renderer, InteropDocuments.AdvancedGraphicsPages[page].Name)))
            return;

        var path = InteropDocuments.Write("advanced-graphics", objectStreams: true);
        var output = Path.ChangeExtension(path, null) + "-" + renderer + "-" + page.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string png;
        if (renderer == "library")
        {
            png = output + ".png";
            using var rendered = Pdf.Rendering.PdfRenderer.RenderPage(PdfDocument.Open(await File.ReadAllBytesAsync(path, XunitCancellationToken)).Pages[page], new Pdf.Rendering.PdfRenderOptions { UseSystemFonts = false });
            await rendered.SaveAsync(png, cancellationToken: XunitCancellationToken);
        }
        else if (renderer == "mutool")
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
