using System.Diagnostics.CodeAnalysis;
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

    public static TheoryData<string, PdfBilevelCompression> BilevelImageRenderers
    {
        get
        {
            var data = new TheoryData<string, PdfBilevelCompression>();
            foreach (var renderer in new[] { "pdftoppm", "mutool", "pdfium" })
            {
                foreach (var compression in Enum.GetValues<PdfBilevelCompression>())
                {
                    data.Add(renderer, compression);
                }
            }

            return data;
        }
    }

    /// <summary>
    /// The bilevel images the library encodes (CCITT Group 4, JBIG2) are decoded by independent decoders: every pixel of
    /// the image is the pixel Poppler, MuPDF and PDFium render at the center of its sample, at 4 pixels per sample (Poppler
    /// smooths the edges of the samples of 1-bit images).
    /// </summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Generating test inputs, and the fixed seed keeps the cases reproducible.")]
    [Theory]
    [MemberData(nameof(BilevelImageRenderers))]
    public async Task BilevelImagesAreDecodedByOtherReaders(string renderer, PdfBilevelCompression compression)
    {
        // Text-like strokes, a dithered gradient, white and black rows, and random noise: runs of every length
        const int Width = 203;
        const int Height = 120;
        const int Stride = (Width + 7) / 8;
        const int Scale = 4;
        var pixels = new byte[Stride * Height];
        var random = new Random(3);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var black = y switch
                {
                    < 30 => (x / 7 + y / 5) % 3 == 0,
                    < 60 => ((x * 37) + (y * 91)) % 255 < x + 20,
                    < 70 => false,
                    < 80 => true,
                    _ => random.Next(2) == 0,
                };
                if (black)
                {
                    pixels[(y * Stride) + (x / 8)] |= (byte)(0x80 >> (x % 8));
                }
            }
        }

        var document = new PdfDocument();
        document.Pages.Add(new PdfSize(Width, Height)).CreateContentBuilder().DrawImage(PdfImage.FromBilevel(pixels, Width, Height, compression), 0, 0, Width, Height);
        var directory = Path.Combine(AppContext.BaseDirectory, "InteropOutput");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"bilevel-{compression}-{Guid.NewGuid():N}.pdf");
        document.Save(path);

        string png;
        if (renderer == "mutool")
        {
            var mutool = await MutoolTool.LocateAsync(XunitCancellationToken);
            png = await mutool.RenderPageAsync(path, 1, 72 * Scale, Path.ChangeExtension(path, ".mutool.png"), XunitCancellationToken);
        }
        else if (renderer == "pdfium")
        {
            var pdfium = await PdfiumTool.LocateAsync(XunitCancellationToken);
            png = await pdfium.RenderPageAsync(await File.ReadAllBytesAsync(path, XunitCancellationToken), 0, Scale, directory, password: null, XunitCancellationToken) ?? throw new InvalidOperationException("PDFium did not render the page");
        }
        else
        {
            var poppler = await PopplerTool.LocateAsync(XunitCancellationToken);
            png = await poppler.RenderPageAsync(path, 1, 72 * Scale, Path.ChangeExtension(path, null) + "-pdftoppm", XunitCancellationToken);
        }

        using var image = Image.Load<Rgb24>(png);
        var frame = image.Frames[0];
        Assert.Equal(Width * Scale, frame.Width);
        Assert.Equal(Height * Scale, frame.Height);
        var differences = 0;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var black = (pixels[(y * Stride) + (x / 8)] & (0x80 >> (x % 8))) != 0;
                if (black != (frame[(x * Scale) + (Scale / 2), (y * Scale) + (Scale / 2)].G < 128))
                {
                    differences++;
                }
            }
        }

        Assert.Equal(0, differences);
    }
}
