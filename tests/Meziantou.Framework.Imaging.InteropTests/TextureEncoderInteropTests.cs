using System.Globalization;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Dds;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// The output of <see cref="DdsEncoder"/> and <see cref="Ktx2Encoder"/> read by independent tools:
/// <list type="bullet">
/// <item><description>every KTX 2.0 format, with mip levels, cube maps, arrays and ZLIB supercompression, passes <c>ktx validate</c> with warnings as errors;</description></item>
/// <item><description>ASTC of every block size decodes in <c>ktx extract</c> (sRGB) and <c>astcenc -dh</c> (UNORM, decode_float16) exactly as the library decodes it;</description></item>
/// <item><description>ETC2 RGB and RGBA and unsigned EAC decode in Apple ImageIO (macOS) exactly as the library decodes them;</description></item>
/// <item><description>the DDS formats OpenImageIO reads decode in <c>oiiotool --dumpdata</c> as the library decodes them: exactly for the 8-bit formats, BC6H and BC7, within the one unit of its integer approximations for BC1 to BC5 (and of its 8-bit reading of R16).</description></item>
/// </list>
/// </summary>
public sealed class TextureEncoderInteropTests
{
    private const int Width = 37;
    private const int Height = 21;

    public static TheoryData<Ktx2TextureFormat> Ktx2Formats => [.. Enum.GetValues<Ktx2TextureFormat>()];

    /// <summary>
    /// Gets the DDS formats OpenImageIO 3.2 reads: neither R16G16B16A16 (UNORM or FLOAT) nor R32G32B32A32_FLOAT, in either
    /// header, nor the signed BC4 and BC5, whose decoding the unit tests check against the specification.
    /// </summary>
    public static TheoryData<DdsTextureFormat> DdsFormats => [.. Enum.GetValues<DdsTextureFormat>().Where(format => format is not (DdsTextureFormat.R16G16B16A16Unorm or DdsTextureFormat.R16G16B16A16Float or DdsTextureFormat.R32G32B32A32Float or DdsTextureFormat.Bc4Snorm or DdsTextureFormat.Bc5Snorm))];

    public static TheoryData<Ktx2TextureFormat> AstcFormats => [.. Enum.GetValues<Ktx2TextureFormat>().Where(format => format >= Ktx2TextureFormat.Astc4x4)];

    [Theory]
    [MemberData(nameof(Ktx2Formats))]
    public async Task Ktx2OutputIsValid(Ktx2TextureFormat format)
    {
        var ktx = await RequiredTools.Ktx2Async();
        var directory = InteropSettings.GetArtifactsDirectory("TextureEncoderInteropTests") / ("ktx2-" + format);
        Directory.CreateDirectory(directory);
        var (signed, floatingPoint, channels) = Describe(format);
        using var image = CreatePattern(Width, Height, seed: (int)format, channels, signed, floatingPoint);
        var single = directory / "mipmaps.ktx2";
        image.Save(single, new Ktx2Encoder { TextureFormat = format, GenerateMipmaps = true, Quality = TextureCompressionQuality.Fast, AllowBitDepthReduction = true });
        await ktx.RunKtxAsync(["validate", "--warnings-as-errors", single], XunitCancellationToken);

        // A cube map array, supercompressed
        using var collection = ImageCollection.Create(ImageCollectionKind.TextureSurfaces);
        using var face = CreatePattern(8, 8, seed: 3, channels, signed, floatingPoint);
        for (var layer = 0; layer < 2; layer++)
        {
            foreach (var cubeFace in Enum.GetValues<CubeMapFace>())
            {
                collection.Add(face, new TextureSurface(layer, cubeFace));
            }
        }

        var cube = directory / "cube-array-zlib.ktx2";
        collection.Save(cube, new Ktx2Encoder { TextureFormat = format, GenerateMipmaps = true, Quality = TextureCompressionQuality.Fast, AllowBitDepthReduction = true, Supercompression = Ktx2Supercompression.Zlib });
        await ktx.RunKtxAsync(["validate", "--warnings-as-errors", cube], XunitCancellationToken);
    }

    [Theory]
    [MemberData(nameof(AstcFormats))]
    public async Task AstcOutputDecodesLikeKtxSoftwareAndAstcenc(Ktx2TextureFormat format)
    {
        var ktx = await RequiredTools.Ktx2Async();
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var directory = InteropSettings.GetArtifactsDirectory("TextureEncoderInteropTests") / ("astc-" + format);
        Directory.CreateDirectory(directory);
        using var image = CreatePattern(Width, Height, seed: 50 + (int)format, channels: 4, signed: false, floatingPoint: false);

        // sRGB: ktx extract decodes in the sRGB operation mode
        var srgb = directory / "srgb.ktx2";
        image.Save(srgb, new Ktx2Encoder { TextureFormat = format });
        var png = directory / "srgb.png";
        File.Delete(png);
        await ktx.RunKtxAsync(["extract", srgb, png], XunitCancellationToken);
        var expected = Assert.Single(await ffmpeg.DecodeToRawFramesAsync(png, "rgba", Width, Height, XunitCancellationToken));
        using (var decoded = Image.Load<Rgba32>(srgb))
        {
            Assert.Equal(expected, GetBytes(decoded));
        }

        // UNORM: the halves of decode_float16 of astcenc
        image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        var unorm = directory / "unorm.ktx2";
        image.Save(unorm, new Ktx2Encoder { TextureFormat = format, Quality = TextureCompressionQuality.Fast });
        var name = format.ToString()["Astc".Length..].Split('x');
        var halves = await ktx.DecodeAstcHalvesAsync(unorm, int.Parse(name[0], CultureInfo.InvariantCulture), int.Parse(name[1], CultureInfo.InvariantCulture), Width, Height, directory, XunitCancellationToken);
        using var decodedHalves = Assert.IsType<Image<RgbaHalf>>(Image.Load(unorm));
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var offset = ((y * Width) + x) * 4;
                Assert.Equal(new RgbaHalf(halves[offset], halves[offset + 1], halves[offset + 2], halves[offset + 3]), decodedHalves.Frames[0][x, y]);
            }
        }
    }

    [Theory]
    [InlineData(Ktx2TextureFormat.Etc2Rgb)]
    [InlineData(Ktx2TextureFormat.Etc2Rgba)]
    [InlineData(Ktx2TextureFormat.EacR11Unorm)]
    [InlineData(Ktx2TextureFormat.EacR11G11Unorm)]
    public async Task Etc2OutputDecodesLikeAppleImageIO(Ktx2TextureFormat format)
    {
        RequiredTools.AppleImageIO();
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var directory = InteropSettings.GetArtifactsDirectory("TextureEncoderInteropTests") / ("imageio-" + format);
        Directory.CreateDirectory(directory);
        var (signed, floatingPoint, channels) = Describe(format);
        using var image = CreatePattern(Width, Height, seed: 70 + (int)format, channels, signed, floatingPoint);
        var file = directory / "texture.ktx2";
        image.Save(file, new Ktx2Encoder { TextureFormat = format });
        var png = directory / "imageio.png";
        File.Delete(png);
        var sips = await ProcessRunner.RunAsync("sips", ["-s", "format", "png", file, "--out", png], XunitCancellationToken);
        Assert.Equal(0, sips.ExitCode);
        var expected = Assert.Single(await ffmpeg.DecodeToRawFramesAsync(png, "rgba64le", Width, Height, XunitCancellationToken));
        using var decoded = Image.Load<Rgba64>(file);
        var actual = GetBytes(decoded);
        if (channels == 1)
        {
            // ImageIO ignores KTXswizzle: it reads the red channel of R11, which the library reads as gray
            for (var i = 0; i < actual.Length; i += 8)
            {
                Assert.Equal(expected.AsSpan(i, 2).ToArray(), actual.AsSpan(i, 2).ToArray());
            }

            return;
        }

        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(DdsFormats))]
    public async Task DdsOutputDecodesLikeOpenImageIO(DdsTextureFormat format)
    {
        var tools = await DdsReferenceTools.LocateAsync(XunitCancellationToken);
        var (signed, floatingPoint, channels) = Describe(format);
        using var image = CreatePattern(Width, Height, seed: 90 + (int)format, channels, signed, floatingPoint);
        var path = InteropSettings.GetArtifactsDirectory("TextureEncoderInteropTests") / ("dds-" + format + ".dds");
        image.Save(path, new DdsEncoder { TextureFormat = format, GenerateMipmaps = true, AllowBitDepthReduction = true });
        var expected = await tools.DumpAsync(path, Width, Height, XunitCancellationToken);

        using var decoded = Image.Load<RgbaFloat>(path);
        var tolerance = format switch
        {
            >= DdsTextureFormat.Bc1Unorm and <= DdsTextureFormat.Bc3UnormSrgb or DdsTextureFormat.Bc4Unorm or DdsTextureFormat.Bc5Unorm => 1.0001 / 255,
            DdsTextureFormat.Bc6HUf16 or DdsTextureFormat.Bc6HSf16 => 0,
            DdsTextureFormat.R16Unorm => 0.5001 / 255,
            _ => 1e-6,
        };
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var pixel = decoded.Frames[0][x, y];
                float[] actual = [pixel.R, pixel.G, pixel.B, pixel.A];
                for (var c = 0; c < channels; c++)
                {
                    // OpenImageIO prints the 8-bit codes of the integer formats, and reduces 16-bit samples to 8 bits
                    var value = expected[(((y * Width) + x) * 4) + c];
                    if (!floatingPoint)
                    {
                        value /= 255;
                    }

                    var matches = tolerance == 0 ? (Half)actual[c] == (Half)value : Math.Abs(actual[c] - value) <= tolerance;
                    Assert.True(matches, $"{path}: ({x}, {y}) channel {c}: the library gives {actual[c]}, OpenImageIO {value}.");
                }
            }
        }
    }

    private static (bool Signed, bool FloatingPoint, int Channels) Describe(Ktx2TextureFormat format) => format switch
    {
        Ktx2TextureFormat.R8 or Ktx2TextureFormat.R16 or Ktx2TextureFormat.Bc4Unorm or Ktx2TextureFormat.EacR11Unorm => (false, false, 1),
        Ktx2TextureFormat.Bc4Snorm or Ktx2TextureFormat.EacR11Snorm => (true, true, 1),
        Ktx2TextureFormat.R8G8 or Ktx2TextureFormat.Bc5Unorm or Ktx2TextureFormat.EacR11G11Unorm => (false, false, 2),
        Ktx2TextureFormat.Bc5Snorm or Ktx2TextureFormat.EacR11G11Snorm => (true, true, 2),
        Ktx2TextureFormat.Bc1Rgb or Ktx2TextureFormat.Etc2Rgb => (false, false, 3),
        Ktx2TextureFormat.Bc6HUfloat => (false, true, 3),
        Ktx2TextureFormat.Bc6HSfloat => (true, true, 3),
        Ktx2TextureFormat.R16G16B16A16Sfloat or Ktx2TextureFormat.R32G32B32A32Sfloat => (false, true, 4),
        _ => (false, false, 4),
    };

    private static (bool Signed, bool FloatingPoint, int Channels) Describe(DdsTextureFormat format) => format switch
    {
        DdsTextureFormat.R8Unorm or DdsTextureFormat.R16Unorm or DdsTextureFormat.Bc4Unorm => (false, false, 1),
        DdsTextureFormat.Bc4Snorm => (true, true, 1),
        DdsTextureFormat.R8G8Unorm or DdsTextureFormat.Bc5Unorm => (false, false, 2),
        DdsTextureFormat.Bc5Snorm => (true, true, 2),
        DdsTextureFormat.Bc6HUf16 => (false, true, 3),
        DdsTextureFormat.Bc6HSf16 => (true, true, 3),
        DdsTextureFormat.R16G16B16A16Float or DdsTextureFormat.R32G32B32A32Float => (false, true, 4),
        _ => (false, false, 4),
    };

    /// <summary>
    /// Creates gradients, waves, edges and noise in the channels a format stores: gray for one channel, a blue of 0 for
    /// two, opaque for three; floating-point samples (linear) in [-1, 1] when signed, up to 8 otherwise.
    /// </summary>
    private static Image CreatePattern(int width, int height, int seed, int channels, bool signed, bool floatingPoint)
    {
        var image = new Image<RgbaFloat>(width, height) { Metadata = { TransferFunction = ColorTransferFunction.Linear } };
        var state = (uint)seed + 11;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                var noise = (state >> 8) / 16777216f;
                var r = (float)(0.5 + (0.45 * Math.Sin((x / 3.0) + seed)));
                var g = x * 1f / (width - 1);
                var b = (x + y) % 7 == 0 ? noise : y * 1f / (height - 1);
                var a = (x / 5) % 3 == 0 ? 1f : Math.Clamp(noise + 0.2f, 0, 1);
                if (channels == 1)
                {
                    (g, b) = (r, r);
                }
                else if (channels == 2)
                {
                    b = 0;
                }

                if (channels < 4)
                {
                    a = 1;
                }

                if (signed)
                {
                    r = (r * 2) - 1;
                    g = channels == 1 ? r : (g * 2) - 1;
                    b = channels == 1 ? r : channels == 2 ? 0 : (b * 2) - 1;
                }
                else if (floatingPoint)
                {
                    r *= 8;
                    g *= 8;
                    b *= channels == 2 ? 0 : 8;
                    if (channels == 1)
                    {
                        (g, b) = (r, r);
                    }
                }

                image.Frames[0][x, y] = new RgbaFloat(r, g, b, a);
            }
        }

        if (floatingPoint)
            return image;

        // Integer formats: 8-bit sRGB pixels
        using (image)
        {
            var integer = image.CloneAs<Rgba32>(new PixelConversionOptions { OutOfRangeSamples = OutOfRangeSampleHandling.Clip });
            integer.Metadata.TransferFunction = ColorTransferFunction.Srgb;
            return integer;
        }
    }

    private static byte[] GetBytes(Image image)
    {
        var bytes = new byte[image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat)];
        image.Frames[0].CopyPixelBytesTo(bytes);
        return bytes;
    }
}
