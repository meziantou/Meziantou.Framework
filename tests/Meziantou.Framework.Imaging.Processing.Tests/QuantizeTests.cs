using System.Security.Cryptography;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Color reduction (<see cref="ImageProcessingExtensions.Quantize(Image, QuantizeOptions?, CancellationToken)"/>),
/// <see cref="PaletteBuilder"/>, <see cref="ImagePalette"/> and <see cref="QuantizeOptions"/>. The pinned hashes are the
/// output of the reduction on this platform: CI runs them on every operating system and architecture, which is what makes
/// the reduction deterministic everywhere.
/// </summary>
public sealed class QuantizeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Quantize_KeepsAnImageWhoseColorsFit()
    {
        using var image = CreateImage(40, 30, (x, y) => new Rgba32((byte)(x / 10 * 60), (byte)(y / 10 * 70), 9));
        var before = Pixels(image.Frames[0]);

        image.Quantize(new QuantizeOptions { Dithering = DitheringMode.Bayer8x8 }, Ct);

        Assert.Equal(before, Pixels(image.Frames[0]));
    }

    [Theory]
    [MemberData(nameof(DitheringModes))]
    public void Quantize_ReducesEveryFrameToMaxColors(DitheringMode dithering)
    {
        using var image = CreateGradient(97, 61);
        image.AppendFrame(image.Frames[0]);
        image.Frames[1].Flip(FlipMode.Horizontal, Ct);

        image.Quantize(new QuantizeOptions { MaxColors = 16, Dithering = dithering }, Ct);

        foreach (var frame in image.Frames)
        {
            Assert.InRange(Pixels(frame).Distinct().Count(), 2, 16);
        }
    }

    [Theory]
    [InlineData(DitheringMode.None, "3E8E1F45EB6E10F0")]
    [InlineData(DitheringMode.FloydSteinberg, "78083FB37AB7338A")]
    [InlineData(DitheringMode.Atkinson, "3153126F0FB86A93")]
    [InlineData(DitheringMode.Burkes, "F54C419C14245CAF")]
    [InlineData(DitheringMode.JarvisJudiceNinke, "F63061F0F82DF3EA")]
    [InlineData(DitheringMode.Sierra, "1A0F6C1993AAE45B")]
    [InlineData(DitheringMode.SierraTwoRow, "9B066984C1407EE2")]
    [InlineData(DitheringMode.SierraLite, "06D3D2771B9D08B6")]
    [InlineData(DitheringMode.Stucki, "514A9D9C279AF35C")]
    [InlineData(DitheringMode.Bayer2x2, "E53FFCA3887106AB")]
    [InlineData(DitheringMode.Bayer4x4, "4049FA6C7729C46E")]
    [InlineData(DitheringMode.Bayer8x8, "53900A2A7FB91A65")]
    public void Quantize_IsDeterministic(DitheringMode dithering, string expectedHash)
    {
        using var image = CreateGradient(97, 61);
        using var clone = image.Clone();

        image.Quantize(new QuantizeOptions { MaxColors = 24, Dithering = dithering }, Ct);
        clone.Quantize(new QuantizeOptions { MaxColors = 24, Dithering = dithering }, Ct);

        Assert.Equal(Pixels(image.Frames[0]), Pixels(clone.Frames[0]));
        Assert.Equal(expectedHash, Hash(image.Frames[0]));
    }

    [Fact]
    public void Quantize_ZeroStrengthIsNoDithering()
    {
        using var plain = CreateGradient(50, 40);
        using var weak = plain.Clone();

        plain.Quantize(new QuantizeOptions { MaxColors = 8 }, Ct);
        weak.Quantize(new QuantizeOptions { MaxColors = 8, Dithering = DitheringMode.FloydSteinberg, DitherStrength = 0 }, Ct);

        Assert.Equal(Pixels(plain.Frames[0]), Pixels(weak.Frames[0]));
    }

    [Fact]
    public void Quantize_StrengthScalesTheDiffusedError()
    {
        using var full = CreateGradient(50, 40);
        using var half = full.Clone();
        using var none = full.Clone();

        full.Quantize(new QuantizeOptions { MaxColors = 8, Dithering = DitheringMode.FloydSteinberg }, Ct);
        half.Quantize(new QuantizeOptions { MaxColors = 8, Dithering = DitheringMode.FloydSteinberg, DitherStrength = 0.5 }, Ct);
        none.Quantize(new QuantizeOptions { MaxColors = 8 }, Ct);

        Assert.NotEqual(Pixels(full.Frames[0]), Pixels(half.Frames[0]));
        Assert.NotEqual(Pixels(none.Frames[0]), Pixels(half.Frames[0]));
    }

    [Fact]
    public void Quantize_ReducesAlphaWithTheColors()
    {
        // Opaque colors, translucent colors and transparent pixels hiding different colors
        using var image = CreateImage(64, 64, (x, y) => y switch
        {
            < 16 => new Rgba32((byte)(x * 4), (byte)(y * 16), 50, 0),
            < 32 => new Rgba32((byte)(x * 4), 100, (byte)(y * 8), (byte)(32 + (x * 3))),
            _ => new Rgba32((byte)(x * 4), (byte)(y * 4), 200),
        });

        image.Quantize(new QuantizeOptions { MaxColors = 32 }, Ct);

        var pixels = Pixels(image.Frames[0]);
        Assert.InRange(pixels.Distinct().Count(), 3, 32);
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var pixel = pixels[(y * 64) + x];
                switch (y)
                {
                    case < 16:
                        Assert.Equal(default, pixel);
                        break;
                    case < 32:
                        Assert.InRange(pixel.A, 1, 254);
                        break;
                    default:
                        Assert.Equal(byte.MaxValue, pixel.A);
                        break;
                }
            }
        }
    }

    [Fact]
    public void Quantize_MapsEveryFrameToAFixedPalette()
    {
        var palette = new ImagePalette([new Rgba32(0, 0, 0), new Rgba32(255, 255, 255), new Rgba32(255, 0, 0), new Rgba32(0, 0, 255)]);
        using var image = CreateGradient(60, 40);
        image.AppendFrame(image.Frames[0]);

        image.Quantize(new QuantizeOptions { Palette = palette, Dithering = DitheringMode.FloydSteinberg }, Ct);

        foreach (var frame in image.Frames)
        {
            foreach (var pixel in Pixels(frame).Distinct())
            {
                Assert.Contains(pixel, palette.Colors.ToArray());
            }
        }
    }

    [Fact]
    public void Quantize_GrayFramesOnlyUseGrayEntries()
    {
        using var image = new Image<Gray8>(32, 8);
        image.Frames[0].ProcessPixelRows(pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                var row = pixels.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new Gray8((byte)(x * 8));
                }
            }
        });

        image.Quantize(new QuantizeOptions { Palette = new ImagePalette([new Rgba32(255, 0, 0), new Rgba32(0, 0, 0), new Rgba32(255, 255, 255)]) }, Ct);

        Assert.Equal(new Gray8(0), image.Frames[0][0, 0]);
        Assert.Equal(new Gray8(255), image.Frames[0][31, 0]);
        Assert.Throws<ArgumentException>(() => image.Quantize(new QuantizeOptions { Palette = new ImagePalette([new Rgba32(255, 0, 0)]) }, Ct));
    }

    [Fact]
    public void Quantize_SixteenBitSamplesGetEightBitValues()
    {
        using var image = new Image<Rgba64>(20, 10);
        image.Frames[0].ProcessPixelRows(pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                var row = pixels.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgba64((ushort)(x * 3001), (ushort)(y * 6007), 1234, ushort.MaxValue);
                }
            }
        });

        image.Quantize(cancellationToken: Ct);

        image.Frames[0].ProcessPixelRows(pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                foreach (var pixel in pixels.GetRowSpan(y))
                {
                    Assert.Equal(0, pixel.R % 257);
                    Assert.Equal(0, pixel.G % 257);
                    Assert.Equal(0, pixel.B % 257);
                }
            }
        });
    }

    [Fact]
    public void Quantize_OneFrame()
    {
        using var image = CreateGradient(30, 20);
        image.AppendFrame(image.Frames[0]);
        var second = Pixels(image.Frames[1]);

        image.Frames[0].Quantize(new QuantizeOptions { MaxColors = 4 }, Ct);

        Assert.InRange(Pixels(image.Frames[0]).Distinct().Count(), 2, 4);
        Assert.Equal(second, Pixels(image.Frames[1]));
    }

    [Fact]
    public void Quantize_ImprovesWithMoreColors()
    {
        using var source = CreateGradient(64, 64);
        var previous = double.NegativeInfinity;
        foreach (var colors in new[] { 4, 16, 64, 256 })
        {
            using var reduced = source.Clone();
            reduced.Quantize(new QuantizeOptions { MaxColors = colors }, Ct);
            var score = ImageSimilarity.ComputePsnr(source.Frames[0], reduced.Frames[0], Ct);
            Assert.True(score > previous, $"{colors} colors: {score} dB, {previous} dB with fewer colors");
            previous = score;
        }
    }

    #region Quantize_Dithering
    [Fact]
    public void Quantize_Dithering()
    {
        // A smooth gradient: 64 x 64 pixels of 4,096 different colors
        using var image = new Image<Rgba32>(64, 64);
        image.Frames[0].ProcessPixelRows(pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                var row = pixels.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgba32((byte)(x * 4), (byte)(y * 4), 128);
                }
            }
        });

        // At most 16 colors, with Floyd–Steinberg error diffusion to hide the bands
        image.Quantize(new QuantizeOptions { MaxColors = 16, Dithering = DitheringMode.FloydSteinberg });

        var colors = new HashSet<Rgba32>();
        image.Frames[0].ProcessPixelRows(pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                colors.UnionWith(pixels.GetRowSpan(y).ToArray());
            }
        });

        Assert.HasCountLessThanOrEqual(16, colors);
    }
    #endregion

    [Fact]
    public void PaletteBuilder_KeepsExactColorsThatFit()
    {
        using var image = CreateImage(8, 2, (x, y) => x < 4 ? new Rgba32(10, 20, 30) : new Rgba32(200, 100, 50, (byte)(y == 0 ? 255 : 0)));
        using var builder = new PaletteBuilder();
        builder.Add(image, Ct);

        var palette = builder.Build();

        Assert.Equal([new Rgba32(10, 20, 30), new Rgba32(200, 100, 50), default], palette.Colors.ToArray());
        Assert.True(palette.HasTransparentColor);
        Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Throws<InvalidOperationException>(() => builder.Add(image, Ct));
    }

    [Fact]
    public void PaletteBuilder_DoesNotDependOnTheOrderOfTheFrames()
    {
        using var first = CreateGradient(50, 30);
        using var second = CreateImage(50, 30, (x, y) => new Rgba32((byte)(255 - (x * 5)), 40, (byte)(y * 8), (byte)(x < 10 ? 128 : 255)));
        using var forward = new PaletteBuilder();
        forward.Add(first, Ct);
        forward.Add(second, Ct);
        using var backward = new PaletteBuilder();
        backward.Add(second, Ct);
        backward.Add(first, Ct);

        Assert.Equal(forward.Build(32).Colors.ToArray(), backward.Build(32).Colors.ToArray());
    }

    [Fact]
    public void PaletteBuilder_RequiresPixels()
    {
        using var builder = new PaletteBuilder();

        Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build(257));
        builder.Dispose();
        Assert.Throws<ObjectDisposedException>(() => builder.Build());
    }

    #region PaletteBuilder_SharedPalette
    [Fact]
    public void PaletteBuilder_SharedPalette()
    {
        // An animation of two frames with different colors
        using var animation = new Image<Rgba32>(32, 32);
        animation.AppendFrame();
        for (var i = 0; i < animation.Frames.Count; i++)
        {
            var shift = i * 100;
            animation.Frames[i].ProcessPixelRows(pixels =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    var row = pixels.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        row[x] = new Rgba32((byte)(x * 8), (byte)((y * 4) + shift), (byte)shift);
                    }
                }
            });
        }

        // One palette for every frame, then every frame maps to it
        ImagePalette palette;
        using (var builder = new PaletteBuilder())
        {
            builder.Add(animation);
            palette = builder.Build(maxColors: 64);
        }

        animation.Quantize(new QuantizeOptions { Palette = palette, Dithering = DitheringMode.Sierra });

        foreach (var frame in animation.Frames)
        {
            var colors = new HashSet<Rgba32>();
            frame.ProcessPixelRows(pixels =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    colors.UnionWith(pixels.GetRowSpan(y).ToArray());
                }
            });

            Assert.True(colors.IsSubsetOf(palette.Colors.ToArray()));
        }
    }
    #endregion

    [Fact]
    public void ImagePalette_MergesDuplicatesAndTransparentColors()
    {
        var palette = new ImagePalette([new Rgba32(1, 2, 3), new Rgba32(9, 9, 9, 0), new Rgba32(1, 2, 3), new Rgba32(4, 5, 6, 0), new Rgba32(1, 2, 3, 100)]);

        Assert.Equal([new Rgba32(1, 2, 3), default, new Rgba32(1, 2, 3, 100)], palette.ToArray());
        Assert.HasCount(3, palette);
        Assert.Equal(default, palette[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => palette[3]);
        Assert.Throws<ArgumentException>(() => new ImagePalette(ReadOnlySpan<Rgba32>.Empty));
        Assert.Throws<ArgumentException>(() => new ImagePalette(Enumerable.Range(0, 257).Select(i => new Rgba32((byte)i, (byte)(i >> 8), 0))));
        Assert.Throws<ArgumentNullException>(() => new ImagePalette((IEnumerable<Rgba32>)null!));
    }

    [Fact]
    public void QuantizeOptions_ValidatesItsSettings()
    {
        var options = QuantizeOptions.Default;
        Assert.Equal(256, options.MaxColors);
        Assert.Equal(DitheringMode.None, options.Dithering);
        Assert.Equal(1d, options.DitherStrength);
        Assert.Null(options.Palette);
        Assert.Throws<ArgumentOutOfRangeException>(() => new QuantizeOptions { MaxColors = 1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new QuantizeOptions { MaxColors = 257 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new QuantizeOptions { Dithering = (DitheringMode)99 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new QuantizeOptions { DitherStrength = -0.01 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new QuantizeOptions { DitherStrength = 1.01 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new QuantizeOptions { DitherStrength = double.NaN });
    }

    [Fact]
    public void Quantize_RejectsALeasedOrDisposedImage()
    {
        var image = CreateGradient(8, 8);
        image.Frames[0].ProcessPixelRows(_ => Assert.Throws<InvalidOperationException>(() => image.Quantize(cancellationToken: Ct)));
        image.Dispose();

        Assert.Throws<ObjectDisposedException>(() => image.Quantize(cancellationToken: Ct));
        Assert.Throws<ArgumentNullException>(() => ((Image)null!).Quantize());
    }

    public static TheoryData<DitheringMode> DitheringModes() => [.. Enum.GetValues<DitheringMode>()];

    private static Image<Rgba32> CreateGradient(int width, int height)
        => CreateImage(width, height, (x, y) => new Rgba32((byte)(x * 255 / (width - 1)), (byte)(y * 255 / (height - 1)), (byte)(((x * 7) + (y * 13)) % 256)));

    private static Image<Rgba32> CreateImage(int width, int height, Func<int, int, Rgba32> color)
    {
        var image = new Image<Rgba32>(width, height);
        image.Frames[0].ProcessPixelRows(pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                var row = pixels.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = color(x, y);
                }
            }
        });

        return image;
    }

    private static Rgba32[] Pixels(ImageFrame frame)
    {
        var typed = (ImageFrame<Rgba32>)frame;
        var pixels = new Rgba32[typed.Width * typed.Height];
        typed.CopyPixelDataTo(pixels);
        return pixels;
    }

    private static string Hash(ImageFrame frame)
    {
        var pixels = Pixels(frame);
        var bytes = new byte[pixels.Length * 4];
        for (var i = 0; i < pixels.Length; i++)
        {
            bytes[4 * i] = pixels[i].R;
            bytes[(4 * i) + 1] = pixels[i].G;
            bytes[(4 * i) + 2] = pixels[i].B;
            bytes[(4 * i) + 3] = pixels[i].A;
        }

        return Convert.ToHexString(SHA256.HashData(bytes))[..16];
    }
}
