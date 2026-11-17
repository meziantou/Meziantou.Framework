using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Meziantou.Framework.Imaging.TestHarness.Dds;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// DDS files compared with independent decoders, on seeded inputs the corpus does not hold:
/// <list type="bullet">
/// <item><description>random BC6H and BC7 blocks decode to the stored samples OpenImageIO prints, exactly (both formats are defined in integers and half-float bit patterns);</description></item>
/// <item><description>the DXT1, DXT5 and uncompressed files ImageMagick writes, with their mipmaps, decode to the values of the specification (<see cref="ReferenceS3tc"/>, <see cref="ReferenceDds"/>) for every surface, and ImageMagick reads the same image within one unit (it interpolates the colors and the BC3 alpha in integers);</description></item>
/// <item><description>on Windows, random blocks of every block-compressed format, and patterns texconv encodes, decode to what DirectXTex decodes, exactly, but for the deviations the corpus records (the midpoints of the three-color mode of BC1, the signed BC5 values within one ulp).</description></item>
/// </list>
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
public sealed class DdsInteropTests
{
    [Theory]
    [InlineData(95u)]
    [InlineData(96u)]
    [InlineData(98u)]
    [InlineData(99u)]
    public async Task RandomBptcBlocksDecodeLikeOpenImageIO(uint dxgiFormat)
    {
        var tools = await DdsReferenceTools.LocateAsync(XunitCancellationToken);
        foreach (var seed in new[] { 1, 2, 3 })
        {
            const int Side = 64;
            var data = CreateRandomBlocks(dxgiFormat, Side, Side, seed);
            var path = InteropSettings.GetArtifactsDirectory("DdsInteropTests") / string.Create(CultureInfo.InvariantCulture, $"bptc-{dxgiFormat}-{seed}.dds");
            await File.WriteAllBytesAsync(path, data, XunitCancellationToken);

            var expected = await tools.DumpAsync(path, Side, Side, XunitCancellationToken);
            using var image = Image.Load<RgbaFloat>(data);
            var isBc7 = dxgiFormat is 98 or 99;
            for (var y = 0; y < Side; y++)
            {
                for (var x = 0; x < Side; x++)
                {
                    var pixel = image.Frames[0][x, y];
                    var offset = ((y * Side) + x) * 4;
                    float[] actual = [pixel.R, pixel.G, pixel.B, pixel.A];
                    for (var c = 0; c < (isBc7 ? 4 : 3); c++)
                    {
                        // BC7: 8-bit integers; BC6H: halves, printed with enough digits to identify them
                        var matches = isBc7 ? Math.Round(actual[c] * 255, MidpointRounding.AwayFromZero) == expected[offset + c] : (Half)actual[c] == (Half)expected[offset + c];
                        Assert.True(matches, $"{path}: ({x}, {y}) channel {c}: the library gives {actual[c]}, OpenImageIO {expected[offset + c]}.");
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData("dxt1", 16, 16, 5)]
    [InlineData("dxt1", 13, 9, 0)]
    [InlineData("dxt5", 32, 8, 6)]
    [InlineData("dxt5", 7, 5, 0)]
    [InlineData("none", 16, 4, 5)]
    [InlineData("none", 7, 3, 0)]
    public async Task FilesWrittenByImageMagickDecodeToTheReferenceOfEverySurface(string compression, int width, int height, int mipmaps)
    {
        var tools = await DdsReferenceTools.LocateAsync(XunitCancellationToken);
        var rgba = CreatePattern(width, height, seed: width + height);
        var path = InteropSettings.GetArtifactsDirectory("DdsInteropTests") / string.Create(CultureInfo.InvariantCulture, $"magick-{compression}-{width}x{height}-{mipmaps}.dds");
        await tools.WriteAsync(rgba, width, height, compression, mipmaps, path, XunitCancellationToken);
        var data = await File.ReadAllBytesAsync(path, XunitCancellationToken);
        var texture = ReferenceDds.Read(data);
        Assert.Equal(Math.Max(1, mipmaps), texture.MipCount);

        using var collection = ImageCollection.Load(data);
        Assert.Equal(texture.Surfaces.Count, collection.Count);
        for (var i = 0; i < texture.Surfaces.Count; i++)
        {
            var surface = texture.Surfaces[i];
            var expected = compression == "none"
                ? ReferenceDds.ReadPixels(data, texture, surface)
                : ReferenceS3tc.Decode(data.AsSpan((int)surface.Offset, (int)surface.Length), surface.Width, surface.Height, compression == "dxt1" ? "BC1" : "BC3");
            using var image = collection[i].Decode<Rgba32>();
            Assert.Equal(new Size(surface.Width, surface.Height), image.Size);
            for (var y = 0; y < surface.Height; y++)
            {
                for (var x = 0; x < surface.Width; x++)
                {
                    var pixel = image.Frames[0][x, y];
                    var offset = ((y * surface.Width) + x) * 4;
                    Assert.Equal(ToRgba32(expected, offset), pixel);
                }
            }
        }

        // ImageMagick reads the top level within one unit: it interpolates the colors of the blocks, and the alpha of BC3, in
        // integers; the 1-bit alpha of BC1 and uncompressed samples are exact
        var magickPath = path + ".rgba";
        var result = await ProcessRunner.RunAsync(tools.MagickPath, [path + "[0]", "-depth", "8", "rgba:" + magickPath], XunitCancellationToken);
        Assert.Equal(0, result.ExitCode);
        var decoded = await File.ReadAllBytesAsync(magickPath, XunitCancellationToken);
        using var top = Image.Load<Rgba32>(data);
        for (var i = 0; i < width * height; i++)
        {
            var pixel = top.Frames[0][i % width, i / width];
            Assert.InRange(Math.Abs(pixel.R - decoded[i * 4]), 0, 1);
            Assert.InRange(Math.Abs(pixel.G - decoded[(i * 4) + 1]), 0, 1);
            Assert.InRange(Math.Abs(pixel.B - decoded[(i * 4) + 2]), 0, 1);
            Assert.InRange(Math.Abs(pixel.A - decoded[(i * 4) + 3]), 0, compression == "dxt5" ? 1 : 0);
        }
    }

    [Theory]
    [InlineData(71u)]
    [InlineData(74u)]
    [InlineData(77u)]
    [InlineData(80u)]
    [InlineData(81u)]
    [InlineData(83u)]
    [InlineData(84u)]
    [InlineData(95u)]
    [InlineData(96u)]
    [InlineData(98u)]
    public async Task RandomBlocksDecodeLikeDirectXTex(uint dxgiFormat)
    {
        var texconv = await LocateTexconvAsync();
        const int Side = 32;
        var data = CreateRandomBlocks(dxgiFormat, Side, Side, seed: (int)dxgiFormat);
        var directory = InteropSettings.GetArtifactsDirectory("DdsInteropTests") / ("texconv-" + dxgiFormat.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        var path = directory / "random.dds";
        await File.WriteAllBytesAsync(path, data, XunitCancellationToken);
        var converted = await DdsReferenceTools.TexconvAsync(texconv, path, ["-m", "1", "-f", "R32G32B32A32_FLOAT"], directory / "out", XunitCancellationToken);
        var texture = ReferenceDds.Read(converted);
        var expected = ReferenceDds.ReadPixels(converted, texture, texture.Surfaces[0]);

        using var image = Image.Load<RgbaFloat>(data);
        var channels = dxgiFormat switch { 80 or 81 => 1, 83 or 84 => 2, 95 or 96 => 3, _ => 4 };
        var midpoints = 0;
        for (var i = 0; i < Side * Side; i++)
        {
            var pixel = image.Frames[0][i % Side, i / Side];
            float[] actual = [pixel.R, pixel.G, pixel.B, pixel.A];
            for (var c = 0; c < channels; c++)
            {
                var (a, e) = (actual[c], expected[(i * 4) + c]);

                // The unsigned normalized formats are compared at 8 bits (texconv gives floats within an ulp of k/255), the
                // signed ones at multiples of 1/127 (DirectXTex multiplies the signed BC5 values by 1/127), BC6H bit for bit
                switch (dxgiFormat)
                {
                    case 95 or 96:
                        Assert.Equal((Half)e, (Half)a);
                        break;
                    case 81 or 84:
                        Assert.Equal(Math.Round(e * 127, MidpointRounding.AwayFromZero), Math.Round(a * 127, MidpointRounding.AwayFromZero));
                        break;
                    default:
                        var (library, reference) = (Math.Round(a * 255, MidpointRounding.AwayFromZero), Math.Round(e * 255, MidpointRounding.AwayFromZero));
                        if (library == reference)
                            break;

                        // DirectXTex rounds the exact midpoints of the three-color mode of BC1 either way, the library up
                        Assert.Equal(71u, dxgiFormat);
                        Assert.Equal(1, Math.Abs(library - reference));
                        midpoints++;
                        break;
                }
            }
        }

        Assert.InRange(midpoints, 0, Side * Side / 8);
    }

    /// <summary>Downloads texconv, which only runs on Windows: the cases are skipped on other systems.</summary>
    private static async Task<string> LocateTexconvAsync()
    {
        if (!OperatingSystem.IsWindows())
            Assert.XunitSkip("texconv (DirectXTex) only runs on Windows.");

        return await DdsReferenceTools.LocateTexconvAsync(XunitCancellationToken);
    }

    private static Rgba32 ToRgba32(double[] values, int offset)
    {
        static byte Byte(double value) => (byte)Math.Round(value * 255, MidpointRounding.AwayFromZero);
        return new Rgba32(Byte(values[offset]), Byte(values[offset + 1]), Byte(values[offset + 2]), Byte(values[offset + 3]));
    }

    /// <summary>An RGBA pattern: gradients, a few flat areas, alpha from 0 to 255.</summary>
    private static byte[] CreatePattern(int width, int height, int seed)
    {
        var random = new Random(seed);
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                rgba[offset] = (byte)(x * 255 / Math.Max(1, width - 1));
                rgba[offset + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
                rgba[offset + 2] = (byte)random.Next(256);
                rgba[offset + 3] = (byte)((x + y) % 3 == 0 ? 255 : random.Next(256));
            }
        }

        return rgba;
    }

    /// <summary>A DX10 file of random blocks; the mode bits of BC7 and BC6H blocks cycle through every mode.</summary>
    private static byte[] CreateRandomBlocks(uint dxgiFormat, int width, int height, int seed)
    {
        var blockLength = dxgiFormat is 71 or 80 or 81 ? 8 : 16;
        var count = ((width + 3) / 4) * ((height + 3) / 4);
        var blocks = new byte[count * blockLength];
        new Random(seed).NextBytes(blocks);
        byte[] bc6Modes = [0x00, 0x01, 0x02, 0x06, 0x0A, 0x0E, 0x12, 0x16, 0x1A, 0x1E, 0x03, 0x07, 0x0B, 0x0F];
        for (var i = 0; i < count; i++)
        {
            var offset = i * blockLength;
            if (dxgiFormat is 98 or 99)
            {
                var mode = i % 8;
                blocks[offset] = (byte)(((blocks[offset] << (mode + 1)) & 0xFF) | (1 << mode));
            }
            else if (dxgiFormat is 95 or 96)
            {
                var mode = bc6Modes[i % bc6Modes.Length];
                blocks[offset] = mode < 2 ? (byte)((blocks[offset] & ~3) | mode) : (byte)((blocks[offset] & ~31) | mode);
            }
        }

        var builder = DdsFileBuilder.Dx10((uint)width, (uint)height, dxgiFormat, blocks);
        return builder.Build();
    }
}
