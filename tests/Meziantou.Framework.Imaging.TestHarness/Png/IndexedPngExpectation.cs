using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.Png;

/// <summary>
/// What an exact indexed PNG (color type 3) of some images must contain, computed from raw reference buffers without the
/// library under test: whether the images can be stored at all (at most 256 distinct combinations of color and alpha, and
/// only samples that 8 bits represent exactly), and then the bit depth, the <c>PLTE</c> data and the <c>tRNS</c> data. The
/// palette order is the documented one: the entries sorted by alpha, then red, green and blue, so that the entries that are
/// not opaque come first and <c>tRNS</c> stores exactly their alpha values.
/// </summary>
public sealed class IndexedPngExpectation
{
    private IndexedPngExpectation(bool isEightBitExact, int colorCount, byte[] palette, byte[] transparency)
    {
        IsEightBitExact = isEightBitExact;
        ColorCount = colorCount;
        Palette = palette;
        Transparency = transparency;
    }

    /// <summary>Gets a value indicating whether every sample is exactly representable in 8 bits (16-bit samples are multiples of 257).</summary>
    public bool IsEightBitExact { get; }

    /// <summary>Gets the number of distinct combinations of red, green, blue and alpha, at the precision of the references.</summary>
    public int ColorCount { get; }

    /// <summary>Gets a value indicating whether an indexed PNG can store the images exactly.</summary>
    public bool IsStorable => IsEightBitExact && ColorCount <= 256;

    /// <summary>Gets the smallest bit depth whose indexes address the palette: 1, 2, 4 or 8.</summary>
    public int BitDepth => ColorCount switch
    {
        <= 2 => 1,
        <= 4 => 2,
        <= 16 => 4,
        _ => 8,
    };

    /// <summary>Gets the expected <c>PLTE</c> data (empty when the images cannot be stored).</summary>
    public ReadOnlyMemory<byte> Palette { get; }

    /// <summary>Gets the expected <c>tRNS</c> data; empty when every entry is opaque (no <c>tRNS</c> chunk).</summary>
    public ReadOnlyMemory<byte> Transparency { get; }

    /// <summary>Gets the expected chunk types between <c>IHDR</c> and the image data.</summary>
    public IReadOnlyList<string> PaletteChunkTypes => Transparency.IsEmpty ? ["PLTE"] : ["PLTE", "tRNS"];

    /// <summary>Computes the expectation of a file holding some images (the frames and the poster of an animation share one palette).</summary>
    /// <param name="images">The images, in any layout.</param>
    /// <returns>The expectation.</returns>
    public static IndexedPngExpectation Create(params IEnumerable<RawPixelBuffer> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        var colors = new HashSet<(int R, int G, int B, int A)>();
        var exact = true;
        foreach (var image in images)
        {
            var scale = image.Layout.ScaleFrom8Bit;
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    var color = GetColor(image, x, y);
                    colors.Add(color);
                    exact &= color.R % scale == 0 && color.G % scale == 0 && color.B % scale == 0 && color.A % scale == 0;
                }
            }
        }

        if (!exact || colors.Count > 256)
            return new IndexedPngExpectation(exact, colors.Count, [], []);

        // Every image has the same precision here, or the 16-bit ones only hold multiples of 257: reduce after the check
        var sorted = colors
            .Select(color => (R: Reduce(color.R), G: Reduce(color.G), B: Reduce(color.B), A: Reduce(color.A)))
            .Distinct()
            .OrderBy(color => color.A).ThenBy(color => color.R).ThenBy(color => color.G).ThenBy(color => color.B)
            .ToList();
        var palette = sorted.SelectMany(color => new[] { (byte)color.R, (byte)color.G, (byte)color.B }).ToArray();
        var transparency = sorted.Where(color => color.A != byte.MaxValue).Select(color => (byte)color.A).ToArray();
        return new IndexedPngExpectation(isEightBitExact: true, sorted.Count, palette, transparency);

        static int Reduce(int sample) => sample > byte.MaxValue ? sample / 257 : sample;
    }

    /// <summary>Converts a reference to straight 8-bit RGBA exactly (gray replicated, opaque without alpha).</summary>
    /// <param name="source">The reference, in any layout.</param>
    /// <returns>The 8-bit RGBA buffer an indexed PNG of the reference decodes to.</returns>
    /// <exception cref="ArgumentException">A 16-bit sample is not a multiple of 257.</exception>
    public static RawPixelBuffer ToRgba8(RawPixelBuffer source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var scale = source.Layout.ScaleFrom8Bit;
        var builder = new RawPixelBufferBuilder(source.Width, source.Height, RawPixelLayout.Rgba8);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var (r, g, b, a) = GetColor(source, x, y);
                if (r % scale != 0 || g % scale != 0 || b % scale != 0 || a % scale != 0)
                    throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The pixel ({x}, {y}) is not exactly representable in 8 bits."), nameof(source));

                builder.SetSample(x, y, 0, r / scale);
                builder.SetSample(x, y, 1, g / scale);
                builder.SetSample(x, y, 2, b / scale);
                builder.SetSample(x, y, 3, a / scale);
            }
        }

        return builder.Build();
    }

    private static (int R, int G, int B, int A) GetColor(RawPixelBuffer image, int x, int y)
    {
        var layout = image.Layout;
        var alpha = layout.HasAlpha ? image.GetSample(x, y, layout.AlphaChannel) : layout.MaxSampleValue;
        if (layout.IsColor)
            return (image.GetSample(x, y, 0), image.GetSample(x, y, 1), image.GetSample(x, y, 2), alpha);

        var gray = image.GetSample(x, y, 0);
        return (gray, gray, gray, alpha);
    }
}
