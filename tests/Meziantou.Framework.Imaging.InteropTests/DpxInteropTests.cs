using System.Buffers.Binary;
using System.Globalization;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// DPX files of the encoder read by the three reference decoders of the pinned meziantou/prebuilt release: FFmpeg (in the
/// native layout of the element), OpenImageIO (<c>oiiotool --dumpdata</c>) and ImageMagick. Each must give the code values
/// the encoder computed, <c>round(v × (2^n - 1) / max)</c>, for every default and explicit depth, in both byte orders. The
/// files of the three tools are fixtures of the golden corpus (<c>dpx/tool-*</c>, <c>cin/tool-*</c>), cross-checked by the
/// corpus generator.
/// </summary>
public sealed class DpxInteropTests
{
    public static TheoryData<string, int, bool> Cases() => new()
    {
        { "rgb", 8, true },
        { "rgb", 8, false },
        { "rgba", 8, true },
        { "gray", 8, true },
        { "rgb", 10, true },
        { "rgb", 10, false },
        { "gray", 10, true },
        { "rgb", 12, true },
        { "rgb", 16, true },
        { "rgba", 16, false },
        { "gray", 16, true },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EncoderOutputDecodesInFFmpeg(string model, int bits, bool bigEndian)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var (path, expected) = await SaveAsync(model, bits, bigEndian, "ffmpeg");
        var (pixelFormat, planar) = (model, bits) switch
        {
            ("rgb", 8) => ("rgb24", false),
            ("rgba", 8) => ("rgba", false),
            ("gray", 8) => ("gray", false),
            ("rgb", 10) => ("gbrp10le", true),
            ("gray", 10) => ("gray10le", false),
            ("rgb", 12) => ("gbrp12le", true),
            ("rgb", 16) => ("rgb48le", false),
            ("rgba", 16) => ("rgba64le", false),
            _ => ("gray16le", false),
        };
        var frame = Assert.Single(await ffmpeg.DecodeToRawFramesAsync(path, pixelFormat, Width, Height, XunitCancellationToken));
        var channels = Channels(model);
        var actual = new uint[Width * Height * channels];
        var pixels = Width * Height;
        for (var i = 0; i < actual.Length; i++)
        {
            if (planar)
            {
                // The green, blue and red planes
                var (pixel, channel) = (i / 3, i % 3);
                var plane = channel switch
                {
                    0 => 2,
                    1 => 0,
                    _ => 1,
                };
                actual[i] = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(((plane * pixels) + pixel) * 2));
            }
            else
            {
                actual[i] = bits == 8 ? frame[i] : BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(i * 2));
            }
        }

        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EncoderOutputDecodesInOpenImageIO(string model, int bits, bool bigEndian)
    {
        var tool = await RequiredTools.DpxAsync();
        var (path, expected) = await SaveAsync(model, bits, bigEndian, "oiio");
        Assert.Equal(expected, await tool.ReadWithOpenImageIOAsync(path, bits, XunitCancellationToken));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EncoderOutputDecodesInImageMagick(string model, int bits, bool bigEndian)
    {
        var tool = await RequiredTools.DpxAsync();
        var (path, expected) = await SaveAsync(model, bits, bigEndian, "magick");
        Assert.Equal(expected, await tool.ReadWithImageMagickAsync(path, model, bits, XunitCancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FloatingPointOutputDecodesInOpenImageIO(bool bigEndian)
    {
        var tool = await RequiredTools.DpxAsync();
        using var image = new Image<RgbaFloat>(Width, Height);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                // Values that seven significant digits print exactly: OpenImageIO prints floats with seven digits
                image.Frames[0][x, y] = new RgbaFloat(x * 0.25f, -y * 0.5f, (x + y) * 2f, 1f);
            }
        }

        var path = InteropSettings.GetArtifactsDirectory("DpxInteropTests") / string.Create(CultureInfo.InvariantCulture, $"float-{(bigEndian ? "be" : "le")}.dpx");
        await image.SaveAsync(path, new DpxEncoder { ByteOrder = bigEndian ? DpxByteOrder.BigEndian : DpxByteOrder.LittleEndian }, XunitCancellationToken);
        var expected = new List<uint>();
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var pixel = image.Frames[0][x, y];
                expected.AddRange([BitConverter.SingleToUInt32Bits(pixel.R), BitConverter.SingleToUInt32Bits(pixel.G), BitConverter.SingleToUInt32Bits(pixel.B)]);
            }
        }

        Assert.Equal(expected.ToArray(), await tool.ReadWithOpenImageIOAsync(path, 32, XunitCancellationToken));
    }

    public static TheoryData<string, int> CineonCases() => new()
    {
        { "rgb", 10 },
        { "gray", 10 },
        { "rgb", 8 },
        { "gray", 8 },
        { "rgb", 16 },
        { "gray", 16 },
    };

    [Theory]
    [MemberData(nameof(CineonCases))]
    public async Task CineonEncoderOutputDecodesInOpenImageIO(string model, int bits)
    {
        var tool = await RequiredTools.DpxAsync();
        var (path, expected) = await SaveCineonAsync(model, bits, "oiio");
        Assert.Equal(expected, await tool.ReadWithOpenImageIOAsync(path, bits, XunitCancellationToken));
    }

    [Theory]
    [MemberData(nameof(CineonCases))]
    public async Task CineonEncoderOutputDecodesInImageMagick(string model, int bits)
    {
        // ImageMagick reads the code values as they are once the colorspace is set to sRGB, or Gray for one channel (it
        // converts from log otherwise). FFmpeg has no Kodak Cineon decoder: its .cin files are Delphine CIN videos
        var tool = await RequiredTools.DpxAsync();
        var (path, expected) = await SaveCineonAsync(model, bits, "magick");
        if (model == "gray" && bits == 10)
        {
            // ImageMagick reads the three 10-bit samples of a word of one channel from the least significant bits, as in a
            // DPX luma element; the draft (left-justified cells), OpenImageIO and the library read them from the most
            // significant bits
            for (var i = 0; i + 2 < expected.Length; i += 3)
            {
                (expected[i], expected[i + 2]) = (expected[i + 2], expected[i]);
            }
        }

        Assert.Equal(expected, await tool.ReadWithImageMagickAsync(path, model, bits, XunitCancellationToken, model == "gray" ? "Gray" : "sRGB"));
    }

    // A line of 12 pixels ends on a 32-bit word at every depth: OpenImageIO does not skip the padding of the lines that
    // do not (dpx/rgb-8-be and dpx/rgb-16-be record it), nor read the last word of a luma line that is not full
    private const int Width = 12;
    private const int Height = 4;

    private static int Channels(string model) => model switch
    {
        "gray" => 1,
        "rgb" => 3,
        _ => 4,
    };

    /// <summary>Saves the pattern of <see cref="SaveAsync"/> with the Cineon encoder and gives the code values it must hold.</summary>
    private static async Task<(FullPath Path, uint[] Expected)> SaveCineonAsync(string model, int bits, string tool)
    {
        // The DPX file of the same pattern and depth holds the same code values
        var (dpx, expected) = await SaveAsync(model, bits, bigEndian: true, tool + "-cin");
        using var image = await Image.LoadAsync(dpx, cancellationToken: XunitCancellationToken);
        var path = dpx.ChangeExtension(".cin");
        await image.SaveAsync(path, new CineonEncoder { BitDepth = bits, AllowBitDepthReduction = true }, XunitCancellationToken);
        return (path, expected);
    }

    /// <summary>Saves a pattern with the encoder and gives the code values it must hold, in the order of the channels.</summary>
    private static async Task<(FullPath Path, uint[] Expected)> SaveAsync(string model, int bits, bool bigEndian, string tool)
    {
        var sixteen = bits > 8;
        using var pattern = new Image<Rgba64>(Width, Height);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var r = (ushort)((x * 13107) + (y * 1234));
                var g = (ushort)(65535 - (x * 9000) - (y * 100));
                var b = (ushort)((x * y * 3001) % 65536);
                var a = (ushort)(model == "rgba" ? 65535 - (y * 10000) - x : 65535);
                pattern.Frames[0][x, y] = new Rgba64(r, g, b, a);
            }
        }

        using Image converted = (model, sixteen) switch
        {
            ("gray", false) => pattern.CloneAs<Gray8>(),
            ("gray", true) => pattern.CloneAs<Gray16>(),
            ("rgb", false) => pattern.CloneAs<Rgb24>(),
            ("rgb", true) => pattern.CloneAs<Rgb48>(),
            ("rgba", false) => pattern.CloneAs<Rgba32>(),
            _ => pattern.CloneAs<Rgba64>(),
        };

        var path = InteropSettings.GetArtifactsDirectory("DpxInteropTests") / string.Create(CultureInfo.InvariantCulture, $"{tool}-{model}-{bits}-{(bigEndian ? "be" : "le")}.dpx");
        await converted.SaveAsync(path, new DpxEncoder { BitDepth = bits, AllowBitDepthReduction = true, ByteOrder = bigEndian ? DpxByteOrder.BigEndian : DpxByteOrder.LittleEndian }, XunitCancellationToken);

        // The code values of the samples: round(v × (2^n - 1) / max)
        using var samples = converted.CloneAs<Rgba64>();
        var max = (1UL << bits) - 1;
        var sourceMax = sixteen ? 65535UL : 255UL;
        var channels = Channels(model);
        var expected = new List<uint>();
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var pixel = samples.Frames[0][x, y];
                ushort[] values = [pixel.R, pixel.G, pixel.B, pixel.A];
                for (var c = 0; c < channels; c++)
                {
                    var value = sixteen ? values[c] : (ulong)(values[c] / 257);
                    expected.Add(sourceMax == max ? (uint)value : (uint)(((2 * value * max) + sourceMax) / (2 * sourceMax)));
                }
            }
        }

        return (path, expected.ToArray());
    }
}
