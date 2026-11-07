using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The animations of the tests of the encoders that write frames as the regions that change (the <c>OptimizeFrames</c>
/// setting of the GIF, APNG and WebP encoders): each one exercises a choice of region, disposal or blending. Every frame
/// has at most 256 distinct colors (a GIF palette holds them exactly), its own duration, and the animation plays 3 times.
/// </summary>
internal static class DeltaFrameAnimations
{
    /// <summary>Gets the names of the animations of <see cref="Create"/>.</summary>
    public static IReadOnlyList<string> Names { get; } = ["sprite-opaque", "sprite-transparent", "blink", "unchanged", "edges", "odd-offsets", "transitions", "translucent", "palettes", "full-palette"];

    /// <summary>Gets the names of <see cref="Names"/>, for a theory.</summary>
    public static TheoryData<string> Cases => [.. Names];

    /// <summary>Creates an animation.</summary>
    /// <param name="name">One of <see cref="Names"/>.</param>
    /// <returns>The animation, 8-bit RGBA.</returns>
    public static Image<Rgba32> Create(string name) => name switch
    {
        // A sprite moves over a background of 144 colors: each frame changes the old and the new place of the sprite
        "sprite-opaque" => Build(40, 30, 8, (i, x, y) => Inside(x, y, 3 + (3 * i), 5 + i, 6, 5) ? new Rgba32(255, 0, (byte)(i * 20), 255) : Background(x, y)),

        // A sprite moves on a transparent canvas: clearing the previous frame leaves only the new place to draw
        "sprite-transparent" => Build(40, 30, 8, (i, x, y) => Inside(x, y, 3 + (3 * i), 5 + i, 6, 5) ? new Rgba32(255, 0, (byte)(i * 20), 255) : default),

        // A square appears every other frame: restoring the canvas of the previous frame removes it
        "blink" => Build(30, 20, 7, (i, x, y) => (i % 2 == 1) && Inside(x, y, 10, 5, 4, 4) ? new Rgba32(0, 255, 0, 255) : Background(x, y)),

        // Frames 1 and 2 are equal, 4 is equal to 3
        "unchanged" => Build(17, 9, 5, (i, x, y) => i is 1 or 2 ? new Rgba32(1, 2, 3, 255) : new Rgba32((byte)(x * 9), (byte)(i == 4 ? 3 : i), 0, 255)),

        // The first pixel, the last pixel, the last column and the first row, then everything
        "edges" => Build(21, 13, 6, (i, x, y) => (i switch { 1 => x == 0 && y == 0, 2 => x == 20 && y == 12, 3 => x == 20 || y == 0, 4 => true, _ => false }) ? new Rgba32(9, 9, (byte)(i * 30), 255) : new Rgba32(200, 100, 50, 255)),

        // A square at odd and even coordinates, on a canvas of odd sizes
        "odd-offsets" => Build(25, 19, 6, (i, x, y) => Inside(x, y, 1 + (2 * i) + (i & 1), 3 + i, 3, 3) ? new Rgba32(10, 20, 30, 255) : new Rgba32(250, 250, 250, 255)),

        // Opaque, a region becomes transparent, opaque again, fully transparent, checkerboards, half transparent
        "transitions" => Build(23, 17, 7, (i, x, y) =>
        {
            var transparent = i switch
            {
                1 => x >= 7 && y >= 4,
                3 => true,
                4 => ((x + y) & 1) == 0,
                5 => ((x + y) & 1) == 1,
                6 => x < 11,
                _ => false,
            };

            return transparent ? default : new Rgba32((byte)(((x * 37) + (i * 11)) % 200), (byte)((((y % 9) * 23) + i) % 200), (byte)(i * 30), 255);
        }),

        // A translucent sprite moves next to an opaque band, on a transparent canvas
        "translucent" => Build(20, 12, 5, (i, x, y) => Inside(x, y, i * 3, 2, 5, 5) ? new Rgba32(100, 150, 200, (byte)(50 + (i * 40))) : (x < 5 ? new Rgba32(1, 1, 1, 255) : default)),

        // Each frame repaints a region with colors of its own (another palette)
        "palettes" => Build(32, 24, 5, (i, x, y) => Inside(x, y, i * 4, i * 3, 12, 10) ? new Rgba32((byte)(i * 50), (byte)(x % 6 * 40), (byte)(y % 5 * 50), 255) : Background(x, y)),

        // Two copies of 256 opaque colors; frame 2 shifts the colors of the left copy and changes the last column: the region
        // covers the canvas, its changed pixels have the 256 colors, and a transparent entry for its unchanged pixels would
        // be a 257th entry
        "full-palette" => Build(32, 16, 3, (i, x, y) => i == 2 && x < 16 ? FullPalette((x + 1) % 16, y) : i == 2 && x == 31 ? FullPalette(0, y) : FullPalette(x % 16, y)),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown animation."),
    };

    /// <summary>Gets the pixels of a frame, 8-bit RGBA.</summary>
    /// <param name="frame">The frame.</param>
    /// <returns>The pixels, row by row.</returns>
    public static byte[] GetPixels(ImageFrame<Rgba32> frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var result = new byte[frame.Width * frame.Height * 4];
        frame.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var offset = ((y * rows.Width) + x) * 4;
                    (result[offset], result[offset + 1], result[offset + 2], result[offset + 3]) = (row[x].R, row[x].G, row[x].B, row[x].A);
                }
            }
        });

        return result;
    }

    private static Rgba32 FullPalette(int x, int y) => new((byte)(x * 16), (byte)(y * 16), 128, 255);

    private static Rgba32 Background(int x, int y) => new((byte)(x % 12 * 20), (byte)(y % 12 * 20), 90, 255);

    private static bool Inside(int x, int y, int left, int top, int width, int height) => x >= left && x < left + width && y >= top && y < top + height;

    private static Image<Rgba32> Build(int width, int height, int frameCount, Func<int, int, int, Rgba32> pixel)
    {
        var image = new Image<Rgba32>(width, height);
        try
        {
            for (var i = 1; i < frameCount; i++)
            {
                image.AppendFrame();
            }

            for (var i = 0; i < frameCount; i++)
            {
                var index = i;
                image.Frames[i].Metadata.Duration = new FrameDuration(i + 1, 20);
                image.Frames[i].ProcessPixelRows(rows =>
                {
                    for (var y = 0; y < rows.Height; y++)
                    {
                        var row = rows.GetRowSpan(y);
                        for (var x = 0; x < row.Length; x++)
                        {
                            row[x] = pixel(index, x, y);
                        }
                    }
                });
            }

            image.Animation = new AnimationMetadata { TotalPlays = 3 };
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }
}
