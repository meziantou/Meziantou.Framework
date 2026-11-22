using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Imaging.Fuzz;

public static partial class ImagingFuzzTargets
{
    /// <summary>The longest text of <see cref="WatermarkTarget"/>.</summary>
    private const int MaxWatermarkText = 32;

    /// <summary>The words of the watermarks, the characters of the subset that draw nothing, and one the subset does not have.</summary>
    /// <remarks>A property: <see cref="All"/>, in another part of the class, may be initialized first.</remarks>
    private static string[] WatermarkTokens =>
    [
        "DRAFT", "CONFIDENTIAL", "SAMPLE", "© 2026", " ", "\n", "\t", "é", "€", "中", "\u0001",
        "\0", "\u0080", "ÿ",
    ];

    /// <summary>
    /// <see cref="ImageProcessingExtensions.AddWatermark(Image, string, ImageWatermarkOptions, CancellationToken)"/> with
    /// the subset of Noto Sans. The input is a byte of flags (bits 0 to 3: the position, modulo 9; bit 4: tiled; bit 5: a
    /// font size; bit 6: a rotation; bit 7: a margin and a tile spacing), a byte of font size (from 0.25 to 64 pixels), a
    /// byte of rotation (3 degrees per unit from -384), a byte of opacity (from 0 to 1), a byte of margin and spacing (in
    /// quarters of a pixel), the image (<see cref="CreateImage"/>, whose pixels are the bytes of the text), then the text.
    /// A rejection is an <see cref="ArgumentException"/> (a character the font does not have) that leaves the image
    /// unchanged. Otherwise the size and the frames are kept, an opacity of 0 changes nothing, a horizontal watermark
    /// against an edge leaves the pixels between that edge and the margin unchanged, and a second run gives the same pixels.
    /// </summary>
    private static void WatermarkTarget(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var fontSize = (FuzzInput.ReadByte(ref data) + 1) / 4.0;
        var rotation = (FuzzInput.ReadByte(ref data) - 128) * 3.0;
        var opacity = FuzzInput.ReadByte(ref data) / 255.0;
        var length = FuzzInput.ReadByte(ref data) / 4.0;
        using var image = CreateImage(ref data, maxSide: 24);
        var text = FuzzText.Decode(data);
        if (text.Length > MaxWatermarkText)
        {
            text = text[..MaxWatermarkText];
            if (char.IsHighSurrogate(text[^1]))
            {
                text = text[..^1];
            }
        }

        if (text.Length == 0)
            return;

        var options = new ImageWatermarkOptions(GlyphMaskFont.Value)
        {
            Position = (WatermarkPosition)((flags & 0xF) % 9),
            IsTiled = (flags & 0x10) != 0,
            FontSize = (flags & 0x20) != 0 ? fontSize : null,
            Rotation = (flags & 0x40) != 0 ? rotation : null,
            Opacity = opacity,
            Color = new Rgba32(255, 0, 0, 200),
            Margin = (flags & 0x80) != 0 ? length : null,
            TileSpacing = (flags & 0x80) != 0 ? length : null,
        };

        using var copy = image.Clone();
        var before = Snapshot(image);
        try
        {
            image.AddWatermark(text, options);
        }
        catch (ArgumentException ex)
        {
            FuzzAssert.Equal(before, Snapshot(image), $"A rejected watermark changed the image: {ex.Message}");
            return;
        }

        FuzzAssert.Equal(copy.Size, image.Size, "The watermark changed the size of the image");
        FuzzAssert.Equal(copy.Frames.Count, image.Frames.Count, "The watermark changed the frame count");
        if (opacity == 0)
        {
            FuzzAssert.Equal(before, Snapshot(image), "A watermark of opacity 0 changed the image");
        }

        if (!options.IsTiled && options.Position != WatermarkPosition.Center && (options.Rotation ?? 0) == 0)
        {
            CheckWatermarkMargins(copy, image, options.Position, options.Margin ?? (Math.Min(image.Width, image.Height) / 40.0));
        }

        copy.AddWatermark(text, options);
        FuzzAssert.Equal(Snapshot(image), Snapshot(copy), "Two watermarks of the same image differ");
    }

    // The pixels closer than the margin (less one pixel, for the edges of the glyphs) to the edges the watermark is
    // placed against are not painted
    private static void CheckWatermarkMargins(Image original, Image watermarked, WatermarkPosition position, double margin)
    {
        var band = (int)Math.Floor(margin) - 1;
        if (band <= 0)
            return;

        var left = position is WatermarkPosition.TopLeft or WatermarkPosition.Left or WatermarkPosition.BottomLeft;
        var right = position is WatermarkPosition.TopRight or WatermarkPosition.Right or WatermarkPosition.BottomRight;
        var top = position is WatermarkPosition.TopLeft or WatermarkPosition.Top or WatermarkPosition.TopRight;
        var bottom = position is WatermarkPosition.BottomLeft or WatermarkPosition.Bottom or WatermarkPosition.BottomRight;
        var width = watermarked.Width;
        var height = watermarked.Height;
        var bytesPerPixel = PixelFormats.GetBytesPerPixel(watermarked.PixelFormat);
        for (var i = 0; i < watermarked.Frames.Count; i++)
        {
            var after = new byte[width * height * bytesPerPixel];
            var expected = new byte[after.Length];
            watermarked.Frames[i].CopyPixelBytesTo(after);
            original.Frames[i].CopyPixelBytesTo(expected);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var outside = (left && x < band) || (right && x >= width - band) || (top && y < band) || (bottom && y >= height - band);
                    if (!outside)
                        continue;

                    var start = ((y * width) + x) * bytesPerPixel;
                    FuzzAssert.True(after.AsSpan(start, bytesPerPixel).SequenceEqual(expected.AsSpan(start, bytesPerPixel)), $"Pixel ({x}, {y}) of frame {i} is in the margin of the watermark and changed");
                }
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetWatermarkSeeds()
    {
        // The cases of the unit tests: every position, a quarter turn, the default size along the diagonal, a tiled text,
        // an animation with a poster, an opacity of 0, spaces only, and a character the font does not have
        for (byte position = 0; position < 9; position++)
        {
            yield return FuzzSeed.FromText($"position-{position}", [(byte)(position | 0xA0), 63, 128, 255, 8, 2, 23, 15, 0], "DRAFT");
        }

        yield return FuzzSeed.FromText("quarter-turn", [0xE1, 63, 158, 255, 8, 0, 23, 23, 0], "C");
        yield return FuzzSeed.FromText("diagonal", [0x00, 0, 0, 255, 0, 0, 23, 11, 0], "CONFIDENTIAL");
        yield return FuzzSeed.FromText("tiled", [0xF0, 31, 128, 255, 16, 4, 23, 23, 0], "SAMPLE");
        yield return FuzzSeed.FromText("tiled-diagonal", [0x10, 0, 0, 128, 0, 1, 23, 15, 0], "DRAFT");
        yield return FuzzSeed.FromText("animation-poster", [0x01, 0, 0, 200, 0, 0, 15, 15, 0x82], "© 2026");
        yield return FuzzSeed.FromText("transparent", [0x18, 0, 0, 0, 0, 6, 15, 15, 0], "DRAFT");
        yield return FuzzSeed.FromText("spaces", [0x00, 0, 0, 255, 0, 2, 7, 7, 0], "   ");
        yield return FuzzSeed.FromText("missing-glyph", [0x00, 0, 0, 255, 0, 0, 7, 7, 0], "A中");
        yield return FuzzSeed.FromText("float-pixels", [0x28, 255, 128, 255, 0, 6, 9, 9, 0], "Wave");
    }
}
