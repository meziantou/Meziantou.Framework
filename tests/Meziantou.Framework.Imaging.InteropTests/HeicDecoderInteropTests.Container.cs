using System.Buffers.Binary;
using System.Globalization;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Heif;
using Meziantou.Framework.Imaging.TestHarness.Hevc;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.InteropTests;

// The properties of the container: sample scaling, transformations, metadata, color description, layouts, errors.
public sealed partial class HeicDecoderInteropTests
{
    /// <summary>Writes a source of a case and encodes it with heif-enc.</summary>
    private static async Task<(FullPath Path, RawPixelBuffer Source)> EncodeSourceAsync(LibHeifTool heif, string name, SourceKind kind, int width, int height, IEnumerable<string> options, int? seed = null)
    {
        var source = CreateSource(kind, width, height, seed ?? StableSeed(name));
        var sourcePath = await SaveSourceAsync(name, source);
        var path = ArtifactPath(name + ".heic");
        await heif.EncodeAsync([.. options, sourcePath], path, XunitCancellationToken);
        return (path, source);
    }

    /// <summary>
    /// Every sample value of a 10-bit and of a 12-bit image, stored without loss, for a monochrome plane and for the three
    /// planes of an identity-matrix image: the library scales each of them to 16 bits exactly,
    /// <c>round(v * 65535 / max)</c>, and heif-dec returns the sample itself.
    /// </summary>
    [Theory]
    [InlineData(10, false)]
    [InlineData(10, true)]
    [InlineData(12, false)]
    [InlineData(12, true)]
    public async Task EverySampleValueIsScaledToSixteenBits(int depth, bool color)
    {
        var heif = await RequiredTools.LibHeifAsync();
        var max = (1 << depth) - 1;
        var (width, height) = (64, (max + 1) / 64);
        var layout = color ? RawPixelLayout.Rgba16Le : RawPixelLayout.Gray16Le;
        var bytes = new byte[layout.GetByteLength(width, height)];
        for (var i = 0; i <= max; i++)
        {
            // heif-enc keeps the most significant bits of the 16-bit samples; each plane of a color image sees the values in
            // another order
            var pixel = bytes.AsSpan(i * layout.BytesPerPixel);
            for (var channel = 0; channel < (color ? 3 : 1); channel++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(pixel[(channel * 2)..], (ushort)(GetSample(i, channel) << (16 - depth)));
            }

            if (color)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(pixel[6..], ushort.MaxValue);
            }
        }

        var name = $"scale-{depth}-{(color ? "rgb" : "gray")}";
        var sourcePath = await SaveSourceAsync(name, RawPixelBuffer.Create(width, height, layout, bytes));
        var path = ArtifactPath(name + ".heic");
        await heif.EncodeAsync(["-L", "-b", depth.ToString(CultureInfo.InvariantCulture), "--no-alpha", sourcePath], path, XunitCancellationToken);

        var info = await heif.GetInfoAsync(path, XunitCancellationToken);
        Assert.Equal((width, height, depth, color ? "RGB" : "monochrome"), (info.Width, info.Height, info.BitDepth, info.ColorSpace));
        var reference = await heif.DecodeAsync(path, bilinearChroma: true, XunitCancellationToken);
        using var image = Image.Load(path);
        Assert.Equal(color ? PixelFormat.Rgba64 : PixelFormat.Gray16, image.PixelFormat);
        var decoded = ToRgba16(ImageSnapshots.CaptureFrame(image.Frames[0]));
        for (var i = 0; i <= max; i++)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                var sample = GetSample(i, color ? channel : 0);
                Assert.Equal(sample, reference.GetSample(i % width, i / width, channel, depth), message: $"{name}: heif-dec does not return the sample {sample} of the image.");
                Assert.Equal(Scale(sample), decoded.GetSample(i % width, i / width, channel), message: $"{name}: the sample {sample} is not scaled to round({sample} * 65535 / {max}).");
            }
        }

        AssertPixels(reference.GetPixels(depth), decoded, ColorBound.Exact, $"{name}: library vs heif-dec {heif.Version}");

        int GetSample(int index, int channel) => channel switch
        {
            0 => index,
            1 => max - index,
            _ => (index * 7) & max,
        };

        ushort Scale(int value) => (ushort)(((value * 65535L) + (max / 2)) / max);
    }

    /// <summary>
    /// <c>--rotate-cw</c>, <c>--flip-h</c> and <c>--flip-v</c> write <c>irot</c> and <c>imir</c> properties (heif-enc reduces
    /// their combination to at most one of each): the library reports the composition as an EXIF orientation and leaves the
    /// pixels as stored, which are those of the source; after <c>AutoOrient()</c>, they are the pixels of heif-dec, which
    /// applies the properties. A lossy 4:2:0 image is rotated the same way: the chroma samples are upsampled as stored.
    /// </summary>
    [Theory]
    [InlineData(-1, false, false, ExifOrientation.TopLeft)]
    [InlineData(-1, true, false, ExifOrientation.TopRight)]
    [InlineData(-1, false, true, ExifOrientation.BottomLeft)]
    [InlineData(-1, true, true, ExifOrientation.BottomRight)]
    [InlineData(0, false, false, ExifOrientation.TopLeft)]
    [InlineData(90, false, false, ExifOrientation.RightTop)]
    [InlineData(90, true, false, ExifOrientation.LeftTop)]
    [InlineData(90, false, true, ExifOrientation.RightBottom)]
    [InlineData(90, true, true, ExifOrientation.LeftBottom)]
    [InlineData(180, false, false, ExifOrientation.BottomRight)]
    [InlineData(180, true, false, ExifOrientation.BottomLeft)]
    [InlineData(180, false, true, ExifOrientation.TopRight)]
    [InlineData(180, true, true, ExifOrientation.TopLeft)]
    [InlineData(270, false, false, ExifOrientation.LeftBottom)]
    [InlineData(270, true, false, ExifOrientation.RightBottom)]
    [InlineData(270, false, true, ExifOrientation.LeftTop)]
    [InlineData(270, true, true, ExifOrientation.RightTop)]
    public async Task RotationAndMirrorAreReportedAsOrientationAndNotApplied(int rotation, bool flipHorizontally, bool flipVertically, ExifOrientation expected)
    {
        var heif = await RequiredTools.LibHeifAsync();
        var transformation = new List<string>();
        if (rotation >= 0)
        {
            transformation.AddRange(["--rotate-cw", rotation.ToString(CultureInfo.InvariantCulture)]);
        }

        if (flipHorizontally)
        {
            transformation.Add("--flip-h");
        }

        if (flipVertically)
        {
            transformation.Add("--flip-v");
        }

        var suffix = string.Create(CultureInfo.InvariantCulture, $"{(rotation < 0 ? "none" : rotation)}{(flipHorizontally ? "-fliph" : "")}{(flipVertically ? "-flipv" : "")}");

        // Without loss, with an alpha plane: the pixels as loaded are those of the source
        var (path, source) = await EncodeSourceAsync(heif, "orientation-rgba-" + suffix, SourceKind.Rgba8, 20, 12, ["-L", .. transformation], seed: 17);
        var still = await AssertStillAsync(heif, path, "orientation-rgba-" + suffix, HeifDecComparison.Exact, source);
        Assert.Equal((20, 12, expected), (still.Width, still.Height, still.Orientation));
        Assert.Equal(expected, Image.Identify(path).Metadata.Orientation);

        // With loss and subsampled chroma, which the stream declares as centered: the comparison with heif-dec is direct
        var (lossyPath, _) = await EncodeSourceAsync(heif, "orientation-420-" + suffix, SourceKind.Rgb8, 96, 64, ["-q", "60", "-p", "x265:chromaloc=1", .. transformation], seed: 19);
        var lossy = await AssertStillAsync(heif, lossyPath, "orientation-420-" + suffix, HeifDecComparison.Subsampled);
        Assert.Equal((96, 64, expected), (lossy.Width, lossy.Height, lossy.Orientation));
    }

    /// <summary>
    /// A clean aperture (<c>clap</c>) that selects whole samples crops the image, before the rotation that follows it. The
    /// property is added to a file of heif-enc, to the color image and to its alpha plane, with
    /// <see cref="HeifFileBuilder"/>. The library upsamples the chroma of the image, then crops the pixels: the chroma
    /// samples outside of the aperture are the neighbors of those inside, at any offset. libheif crops the planes first,
    /// which it cannot do at an odd offset in subsampled planes without moving the chroma samples: these cases are only
    /// compared with the planes of the reference decoders.
    /// </summary>
    [Theory]
    [InlineData("rgb", 3, 5, 17, 9, -1, true)]
    [InlineData("444", 3, 5, 17, 9, -1, true)]
    [InlineData("444", 0, 0, 95, 63, -1, true)]
    [InlineData("444", 1, 1, 95, 63, -1, true)]
    [InlineData("420", 4, 2, 16, 10, -1, true)]
    [InlineData("420", 6, 8, 21, 13, -1, true)]
    [InlineData("420", 5, 3, 20, 11, -1, false)]
    [InlineData("420-centered", 4, 2, 16, 10, -1, true)]
    [InlineData("420-centered", 7, 9, 33, 21, -1, false)]
    [InlineData("422", 2, 3, 20, 11, -1, true)]
    [InlineData("422", 3, 2, 20, 11, -1, false)]
    [InlineData("400", 5, 7, 9, 3, -1, true)]
    [InlineData("rgb", 3, 5, 17, 9, 90, true)]
    [InlineData("444", 3, 5, 17, 9, 270, true)]
    [InlineData("444", 3, 5, 17, 9, 180, true)]
    public async Task CleanApertureCropsTheImage(string format, int x, int y, int width, int height, int rotation, bool compareWithHeifDec)
    {
        const int SourceWidth = 96;
        const int SourceHeight = 64;
        var heif = await RequiredTools.LibHeifAsync();
        var name = string.Create(CultureInfo.InvariantCulture, $"clap-{format}-{x}-{y}-{width}x{height}{(rotation < 0 ? "" : "-rotate" + rotation.ToString(CultureInfo.InvariantCulture))}");
        var (kind, options, comparison) = format switch
        {
            "rgb" => (SourceKind.Rgba8, (string[])["-L"], HeifDecComparison.Exact),
            "444" => (SourceKind.Rgba8, ["-q", "70", "-p", "chroma=444"], HeifDecComparison.Matrix),
            "420" => (SourceKind.Rgba8, ["-q", "70"], HeifDecComparison.SitingDiffers),
            "420-centered" => (SourceKind.Rgba8, ["-q", "70", "-p", "x265:chromaloc=1"], HeifDecComparison.SitingDiffers),
            "422" => (SourceKind.Rgba8, ["-q", "70", "-p", "chroma=422"], HeifDecComparison.SitingDiffers),
            "400" => (SourceKind.Gray8, ["-q", "70"], HeifDecComparison.Exact),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown format."),
        };

        string[] rotationOptions = rotation < 0 ? [] : ["--rotate-cw", rotation.ToString(CultureInfo.InvariantCulture)];
        var (path, _) = await EncodeSourceAsync(heif, name + "-source", kind, SourceWidth, SourceHeight, [.. options, .. rotationOptions], seed: 23);

        // The size of the aperture, then the offset of its center from the center of the image, as fractions
        var builder = HeifFileBuilder.From(await File.ReadAllBytesAsync(path, XunitCancellationToken));
        var aperture = builder.AddProperty(HeifFileBuilder.CleanAperture((uint)width, 1, (uint)height, 1, (2 * x) + width - SourceWidth, 2, (2 * y) + height - SourceHeight, 2));
        foreach (var item in builder.Items.Where(item => item.Type == "hvc1"))
        {
            // Before the rotation: the transformations are applied in the order of their associations
            var position = item.Associations.Select((association, index) => (association, index)).Where(entry => GetPropertyType(builder, entry.association.PropertyIndex) is "irot" or "imir").Select(entry => (int?)entry.index).FirstOrDefault() ?? item.Associations.Count;
            item.Associations.Insert(position, (aperture, true));
        }

        var croppedPath = ArtifactPath(name + ".heic");
        await File.WriteAllBytesAsync(croppedPath, builder.Build(), XunitCancellationToken);
        var info = await heif.GetInfoAsync(croppedPath, XunitCancellationToken);
        Assert.Equal<(int, int, int, int)?>((x, y, SourceWidth - x - width, SourceHeight - y - height), info.Crop);

        var still = await AssertStillAsync(heif, croppedPath, name, comparison, compareWithHeifDec: compareWithHeifDec);
        Assert.Equal((width, height), (still.Width, still.Height));
        Assert.Equal(new Size(width, height), Image.Identify(croppedPath).Size);
    }

    /// <summary>
    /// A clean aperture that follows a rotation in the associations of the item crops the rotated image: the transformations
    /// apply in the order of their associations (ISO/IEC 23008-12, section 6.5.1). MIAF files always crop first
    /// (ISO/IEC 23000-22, section 7.3.6.7), so no writer at hand produces this order: the properties of a file of heif-enc
    /// are reordered with <see cref="HeifFileBuilder"/>. heif-dec applies them in order. The library reports an orientation
    /// for pixels that it returns as they are stored: it keeps the part of the stored image that the aperture shows once
    /// the image is rotated, so that its pixels are those of heif-dec after <c>AutoOrient()</c>.
    /// </summary>
    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public async Task CleanApertureAfterARotationCropsTheRotatedImage(int rotation)
    {
        var heif = await RequiredTools.LibHeifAsync();
        var name = string.Create(CultureInfo.InvariantCulture, $"clap-after-rotate{rotation}");
        var (path, _) = await EncodeSourceAsync(heif, name + "-source", SourceKind.Rgb8, 96, 64, ["-L", "--rotate-cw", rotation.ToString(CultureInfo.InvariantCulture)], seed: 29);

        // The rotated image has 64 columns and 96 rows after a quarter turn; the aperture keeps 20 x 30 samples of it, from (7, 11)
        var (rotatedWidth, rotatedHeight) = rotation is 180 ? (96, 64) : (64, 96);
        var builder = HeifFileBuilder.From(await File.ReadAllBytesAsync(path, XunitCancellationToken));
        var item = builder.Items.Single(item => item.Type == "hvc1");
        item.Associations.Add((builder.AddProperty(HeifFileBuilder.CleanAperture(20, 1, 30, 1, (2 * 7) + 20 - rotatedWidth, 2, (2 * 11) + 30 - rotatedHeight, 2)), true));
        var croppedPath = ArtifactPath(name + ".heic");
        await File.WriteAllBytesAsync(croppedPath, builder.Build(), XunitCancellationToken);

        // The file is valid for the reference decoder
        var reference = await heif.DecodeAsync(croppedPath, bilinearChroma: true, XunitCancellationToken);
        Assert.Equal((20, 30), (reference.Width, reference.Height));

        // The pixels are stored before the rotation: the image that is loaded has the size of the aperture as it is stored
        var storedSize = rotation is 180 ? new Size(20, 30) : new Size(30, 20);
        using var image = Image.Load(croppedPath);
        Assert.Equal(ImageFormat.Heic, image.Metadata.SourceFormat);
        Assert.NotEqual(ExifOrientation.TopLeft, image.Metadata.Orientation);
        Assert.Equal(storedSize, image.Size);
        Assert.Equal(storedSize, Image.Identify(croppedPath).Size);

        // The file stores its source without loss: the two decoders give the same pixels
        image.AutoOrient(XunitCancellationToken);
        Assert.Equal(new Size(20, 30), image.Size);
        var difference = AssertPixels(WithoutAlpha(reference.GetPixels(8)), WithoutAlpha(ToRgba16(ImageSnapshots.CaptureFrame(image.Frames[0]))), ColorBound.Exact, $"{name}: library after AutoOrient() vs heif-dec");
        WriteOutput(string.Create(CultureInfo.InvariantCulture, $"{name}: library after AutoOrient() vs heif-dec max {difference.Max}"));
    }

    /// <summary>
    /// The ICC profile, the EXIF block and the XMP packet of the PNG source, which heif-enc copies to the file, are returned
    /// byte for byte (heif-enc resets the orientation of the EXIF block, which is not the orientation of the image in any
    /// case: <c>irot</c> and <c>imir</c> are its only source). With an ICC profile, heif-enc writes no <c>nclx</c> box unless it is asked for both: the color
    /// description then comes from the bitstream.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MetadataPayloadsAreReadByteForByte(bool twoColorBoxes)
    {
        var heif = await RequiredTools.LibHeifAsync();
        var name = $"metadata-{(twoColorBoxes ? "icc-and-nclx" : "icc")}";
        var source = CreateSource(SourceKind.Rgb8, 80, 64, seed: 29);
        var plainPath = await SaveSourceAsync(name + "-plain", source);

        // A display profile, a little-endian TIFF structure whose only tag is Orientation = 6 (RightTop), and an XMP packet
        var icc = IccProfileBuilder.Rgb(IccProfileBuilder.DisplayP3Colorants, IccProfileBuilder.SrgbCurve());
        byte[] exif = [(byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0, 1, 0, 0x12, 0x01, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0];
        var xmp = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><lorem>ipsum</lorem></x:xmpmeta>"u8.ToArray();
        var sourcePath = ArtifactPath(name + ".png");
        await File.WriteAllBytesAsync(sourcePath, AddPngMetadata(await File.ReadAllBytesAsync(plainPath, XunitCancellationToken), icc, exif, xmp), XunitCancellationToken);
        var path = ArtifactPath(name + ".heic");
        await heif.EncodeAsync(["-q", "70", "-p", "chroma=444", .. (twoColorBoxes ? ["--enable-two-colr-boxes"] : Array.Empty<string>()), sourcePath], path, XunitCancellationToken);

        // heif-info and the reader of the harness find the payloads; the EXIF item starts with a four-byte offset
        var info = await heif.GetInfoAsync(path, XunitCancellationToken);
        Assert.Equal<(int?, int?)>((exif.Length + 4, xmp.Length), (info.ExifLength, info.XmpLength));
        Assert.Equal(["prof"], info.ColorProfiles);
        Assert.Equal(twoColorBoxes, info.MatrixCoefficients is not null);
        var reader = ReferenceHeif.Parse(await File.ReadAllBytesAsync(path, XunitCancellationToken));
        var primary = reader.PrimaryItem!;
        Assert.Equal(icc, primary.IccProfile?.ToArray());
        Assert.Equal(twoColorBoxes, primary.Nclx is not null);
        var descriptions = reader.FindDescriptions(primary.Id);

        // heif-enc sets the orientation of the EXIF block it copies to 1 (TopLeft), and does not rotate the image
        exif[18] = 1;
        Assert.Equal(exif, reader.ReadExifPayload(descriptions.Single(item => item.Type == "Exif")));
        Assert.Equal(xmp, reader.ReadItemData(descriptions.Single(item => item.Type == "mime")));

        using (var image = Image.Load(path))
        {
            Assert.Equal(icc, image.Metadata.IccProfile?.Data.ToArray());
            Assert.Equal(exif, image.Metadata.ExifProfile?.Data.ToArray());
            Assert.Equal(xmp, image.Metadata.XmpProfile?.Data.ToArray());
            Assert.Equal(ExifOrientation.TopLeft, image.Metadata.Orientation);
        }

        // The properties of the image (color profile, transformations) are in its header; the EXIF and XMP items are data of
        // the file, which a full scan reaches
        var header = Image.Identify(path, new ImageIdentifyOptions { Mode = ImageIdentifyMode.Header });
        Assert.Equal(icc, header.Metadata.IccProfile?.Data.ToArray());
        Assert.Equal(ExifOrientation.TopLeft, header.Metadata.Orientation);
        var scanned = Image.Identify(path, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.Equal(icc, scanned.Metadata.IccProfile?.Data.ToArray());
        Assert.Equal(exif, scanned.Metadata.ExifProfile?.Data.ToArray());
        Assert.Equal(xmp, scanned.Metadata.XmpProfile?.Data.ToArray());
        Assert.Equal(ExifOrientation.TopLeft, scanned.Metadata.Orientation);

        // The pixels: the matrix of the "nclx" box, or of the bitstream
        var still = await AssertStillAsync(heif, path, name, HeifDecComparison.Matrix);
        Assert.True(still.Color.Signal.HasColorDescription, $"{name}: the bitstream has no color description.");
        Assert.NotNull(still.Cicp);
    }

    /// <summary>
    /// The color description without <c>nclx</c> box: the box is removed from a file of heif-enc with
    /// <see cref="HeifFileBuilder"/>, and the video signal description of the bitstream, which x265 writes from the same
    /// values, then describes the image: the library reports it and converts the samples with it. The two descriptions of
    /// a file are not always the same: for unspecified matrix coefficients, heif-enc converts with BT.601 and says so in
    /// the bitstream only; for a monochrome image in limited range, it only says so in the box.
    /// </summary>
    [Theory]
    [InlineData("bt709-limited-444", SourceKind.Rgb8, new[] { "-q", "75", "-p", "chroma=444", "--matrix_coefficients", "1", "--full_range_flag", "0" }, HeifDecComparison.Matrix, 1, false, 1, false)]
    [InlineData("bt709-limited-420", SourceKind.Rgb8, new[] { "-q", "75", "-p", "x265:chromaloc=1", "--matrix_coefficients", "1", "--full_range_flag", "0" }, HeifDecComparison.Subsampled, 1, false, 1, false)]
    [InlineData("bt2020-limited-444-10", SourceKind.Rgb16, new[] { "-q", "75", "-b", "10", "--no-alpha", "-p", "chroma=444", "--matrix_coefficients", "9", "--colour_primaries", "9", "--transfer_characteristic", "16", "--full_range_flag", "0" }, HeifDecComparison.Matrix, 9, false, 9, false)]
    [InlineData("bt601-full-422", SourceKind.Rgb8, new[] { "-q", "75", "-p", "chroma=422" }, HeifDecComparison.SitingDiffers, 6, true, 6, true)]
    [InlineData("identity-full-444", SourceKind.Rgb8, new[] { "-L" }, HeifDecComparison.Exact, 0, true, 0, true)]
    [InlineData("unspecified-full-444", SourceKind.Rgb8, new[] { "-q", "75", "-p", "chroma=444", "--matrix_coefficients", "2" }, HeifDecComparison.Matrix, 2, true, 6, true)]
    [InlineData("bt709-full-420-alpha", SourceKind.Rgba8, new[] { "-q", "75", "-p", "x265:chromaloc=1", "--matrix_coefficients", "1" }, HeifDecComparison.Subsampled, 1, true, 1, true)]
    public async Task ColorDescriptionComesFromTheBitstreamWithoutNclxBox(string label, SourceKind kind, string[] options, HeifDecComparison comparison, int boxMatrix, bool boxFullRange, int streamMatrix, bool streamFullRange)
    {
        var heif = await RequiredTools.LibHeifAsync();
        var name = "nclx-removed-" + label;
        var (path, _) = await EncodeSourceAsync(heif, name + "-source", kind, 80, 64, options);
        var original = await AssertStillAsync(heif, path, name + "-source", comparison);
        Assert.Equal((boxMatrix, boxFullRange), (original.Cicp?.MatrixCoefficients, original.Cicp?.FullRange));

        var builder = HeifFileBuilder.From(await File.ReadAllBytesAsync(path, XunitCancellationToken));
        RemoveProperties(builder, "colr");
        var strippedPath = ArtifactPath(name + ".heic");
        await File.WriteAllBytesAsync(strippedPath, builder.Build(), XunitCancellationToken);
        Assert.Null(ReferenceHeif.Parse(await File.ReadAllBytesAsync(strippedPath, XunitCancellationToken)).PrimaryItem!.Nclx);

        var stripped = await AssertStillAsync(heif, strippedPath, name, comparison);
        Assert.True(stripped.Color.Signal is { HasVideoSignalType: true, HasColorDescription: true }, $"{name}: the bitstream has no color description.");
        Assert.Equal((streamMatrix, streamFullRange), (stripped.Cicp?.MatrixCoefficients, stripped.Cicp?.FullRange));
        if ((boxMatrix, boxFullRange) == (streamMatrix, streamFullRange) || boxMatrix == 2)
        {
            // The same conversion, with or without the box: unspecified matrix coefficients are converted as BT.601
            AssertPixels(original.Pixels, stripped.Pixels, ColorBound.Exact, $"{name}: the pixels without 'nclx' box vs the pixels with it");
        }
    }

    /// <summary>
    /// A monochrome image whose <c>nclx</c> box and bitstream disagree on the range (what heif-enc writes for
    /// <c>--full_range_flag 0</c>): the box wins, and the bitstream applies once the box is removed.
    /// </summary>
    [Fact]
    public async Task RangeOfAMonochromeImageComesFromTheNclxBoxThenFromTheBitstream()
    {
        var heif = await RequiredTools.LibHeifAsync();
        const string Name = "nclx-removed-gray-limited";
        var (path, _) = await EncodeSourceAsync(heif, Name + "-source", SourceKind.Gray8, 80, 64, ["-q", "75", "--full_range_flag", "0"]);
        var original = await AssertStillAsync(heif, path, Name + "-source", HeifDecComparison.MonochromeRangeIgnored);
        Assert.Equal<bool?>(false, original.Cicp?.FullRange);
        Assert.True(original.Color.Signal is { HasVideoSignalType: true, FullRange: true });

        var builder = HeifFileBuilder.From(await File.ReadAllBytesAsync(path, XunitCancellationToken));
        RemoveProperties(builder, "colr");
        var strippedPath = ArtifactPath(Name + ".heic");
        await File.WriteAllBytesAsync(strippedPath, builder.Build(), XunitCancellationToken);
        var stripped = await AssertStillAsync(heif, strippedPath, Name, HeifDecComparison.Exact);

        // The bitstream has no color description, only a range: nothing is reported, and the samples are not expanded
        Assert.Null(stripped.Cicp);
        Assert.False(original.Pixels.Span.SequenceEqual(stripped.Pixels.Span), $"{Name}: the range of the box does not change the pixels.");
    }

    /// <summary>
    /// The layouts of the container that heif-enc does not write: a file of heif-enc is laid out differently with
    /// <see cref="HeifFileBuilder"/>, read back by heif-info and heif-dec, and must decode to the pixels of the original.
    /// </summary>
    [Theory]
    [InlineData("mdat-before-meta")]
    [InlineData("item-data-box")]
    [InlineData("several-extents")]
    [InlineData("large-box-sizes")]
    [InlineData("mdat-to-end-of-file")]
    [InlineData("generic-major-brand")]
    [InlineData("free-boxes")]
    [InlineData("two-byte-nal-lengths")]
    [InlineData("mpeg-b-alpha-type")]
    public async Task ContainerLayoutsDecodeAsTheOriginalFile(string layout)
    {
        var heif = await RequiredTools.LibHeifAsync();
        var name = "layout-" + layout;
        var (path, _) = await EncodeSourceAsync(heif, name + "-source", SourceKind.Rgba8, 96, 64, ["-q", "70", "-p", "chroma=444"], seed: 31);
        var original = await AssertStillAsync(heif, path, name + "-source", HeifDecComparison.Matrix);

        var builder = HeifFileBuilder.From(await File.ReadAllBytesAsync(path, XunitCancellationToken));
        switch (layout)
        {
            case "mdat-before-meta":
                builder.Layout.Clear();
                builder.Layout.Add(HeifFileBuilder.FileTypeBox);
                builder.Layout.Add(HeifFileBuilder.MediaDataBox);
                builder.Layout.Add(HeifFileBuilder.MetaBox);
                break;

            case "item-data-box":
                foreach (var item in builder.Items)
                {
                    item.Storage = HeifFileBuilder.ItemStorage.ItemData;
                }

                break;

            case "several-extents":
                foreach (var item in builder.Items)
                {
                    item.ExtentLengths.Add(1);
                    item.ExtentLengths.Add(item.Data.Length / 3);
                    item.ExtentLengths.Add(item.Data.Length - 1 - (item.Data.Length / 3));
                }

                break;

            case "large-box-sizes":
                // Not the file type box: libheif does not recognize a file whose first box has a 64-bit size (see below)
                builder.LargeSizeBoxes.Add(HeifFileBuilder.MetaBox);
                builder.LargeSizeBoxes.Add(HeifFileBuilder.MediaDataBox);
                break;

            case "mdat-to-end-of-file":
                builder.MediaDataExtendsToEnd = true;
                break;

            case "generic-major-brand":
                // "mif1" is the brand of any HEIF image: the "heic" compatible brand tells the codec
                builder.MajorBrand = "mif1";
                builder.CompatibleBrands.Clear();
                builder.CompatibleBrands.Add("mif1");
                builder.CompatibleBrands.Add("miaf");
                builder.CompatibleBrands.Add("heic");
                break;

            case "free-boxes":
                builder.ExtraBoxes.Add("free", HeifFileBuilder.Box("free", new byte[37]));
                builder.ExtraBoxes.Add("skip", HeifFileBuilder.Box("skip", [1, 2, 3]));
                builder.Layout.Insert(1, "free");
                builder.Layout.Insert(3, "skip");
                builder.Layout.Add("free");
                break;

            case "two-byte-nal-lengths":
                // The NAL units of the items are preceded by their length on two bytes instead of four
                foreach (var item in builder.Items.Where(item => item.Type == "hvc1"))
                {
                    var position = item.Associations.Select((association, position) => (association, position)).Single(entry => GetPropertyType(builder, entry.association.PropertyIndex) == "hvcC").position;
                    var box = (byte[])builder.Properties[item.Associations[position].PropertyIndex - 1].Clone();
                    item.Data = ReferenceHevcStream.ToLengthPrefixed(ReferenceHeif.HevcConfiguration.Parse(box.AsSpan(8)).SplitNalUnits(item.Data), nalLengthSize: 2);

                    // lengthSizeMinusOne: the two low bits of the 22nd byte of the record
                    box[8 + 21] = (byte)((box[8 + 21] & 0xFC) | 1);
                    item.Associations[position] = (builder.AddProperty(box), true);
                }

                break;

            case "mpeg-b-alpha-type":
                // The auxiliary type of MIAF, which is not specific to a codec, instead of the type of HEVC auxiliary images
                var auxiliaryType = builder.AddProperty(HeifFileBuilder.AuxiliaryType(ReferenceHeif.AlphaAuxiliaryType));
                var alphaItem = builder.Items.Single(item => item.Associations.Any(association => GetPropertyType(builder, association.PropertyIndex) == "auxC"));
                var index = alphaItem.Associations.Select((association, position) => (association, position)).Single(entry => GetPropertyType(builder, entry.association.PropertyIndex) == "auxC").position;
                alphaItem.Associations[index] = (auxiliaryType, true);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(layout), layout, "Unknown layout.");
        }

        var variantPath = ArtifactPath(name + ".heic");
        var bytes = builder.Build();
        await File.WriteAllBytesAsync(variantPath, bytes, XunitCancellationToken);

        // The file has the layout that was asked for
        var reader = ReferenceHeif.Parse(bytes);
        var boxes = reader.TopLevelBoxes.Select(box => box.Type).ToList();
        var codedItems = reader.Items.Where(item => item.Type == "hvc1").ToList();
        Assert.HasCount(2, codedItems);
        switch (layout)
        {
            case "mdat-before-meta":
                Assert.Equal(["ftyp", "mdat", "meta"], boxes);
                break;

            case "item-data-box":
                Assert.All(codedItems, item => Assert.Equal(1, item.ConstructionMethod));
                break;

            case "several-extents":
                Assert.All(codedItems, item => Assert.HasCount(3, item.Extents));
                break;

            case "large-box-sizes":
                Assert.Equal([false, true, true], reader.TopLevelBoxes.Select(box => box.HasLargeSize));
                break;

            case "mdat-to-end-of-file":
                Assert.True(reader.TopLevelBoxes[^1] is { Type: "mdat", ExtendsToEnd: true });
                break;

            case "generic-major-brand":
                Assert.Equal(("mif1", true), (reader.MajorBrand, reader.HasBrand("heic")));
                break;

            case "free-boxes":
                Assert.Equal(["ftyp", "free", "meta", "skip", "mdat", "free"], boxes);
                break;

            case "two-byte-nal-lengths":
                Assert.All(codedItems, item => Assert.Equal(2, item.HevcConfiguration?.NalUnitLengthSize));
                break;

            default:
                Assert.Equal(ReferenceHeif.AlphaAuxiliaryType, reader.AlphaItem?.AuxiliaryType);
                break;
        }

        Assert.Equal(ImageFormat.Heic, Image.DetectFormat(bytes));
        // libheif reads the NAL units of an item as if their length always took four bytes: it does not decode that file
        var variant = await AssertStillAsync(heif, variantPath, name, HeifDecComparison.Matrix, compareWithHeifDec: layout != "two-byte-nal-lengths");
        AssertPixels(original.Pixels, variant.Pixels, ColorBound.Exact, $"{name}: the pixels of the file laid out differently vs the pixels of the original");

        // The same pixels from a stream that cannot seek
        await using var stream = new ForwardOnlyStream(bytes);
        using var streamed = await Image.LoadAsync(stream, cancellationToken: XunitCancellationToken);
        AssertPixels(original.Pixels, ToRgba16(ImageSnapshots.CaptureFrame(streamed.Frames[0])), ColorBound.Exact, $"{name}: the pixels read from a forward-only stream vs the pixels of the original");
        if (layout == "large-box-sizes")
        {
            // A file type box with a 64-bit size too: heif-info does not recognize the file ("MIME type: unknown"), so only
            // the library reads it
            builder.LargeSizeBoxes.Add(HeifFileBuilder.FileTypeBox);
            var large = builder.Build();
            Assert.Equal(ImageFormat.Heic, Image.DetectFormat(large));
            using var image = Image.Load(large.AsSpan());
            AssertPixels(original.Pixels, ToRgba16(ImageSnapshots.CaptureFrame(image.Frames[0])), ColorBound.Exact, $"{name}: the pixels of the file whose file type box has a 64-bit size vs the pixels of the original");
        }
    }

    /// <summary>
    /// The alpha plane of a grid written by <c>heif-enc --cut-tiles</c>: an auxiliary image of each tile, and none for the
    /// grid, which is the image. libheif composes the alpha planes of the tiles; the library presents the image that the
    /// file designates and reads its own auxiliary images, as Apple ImageIO does (<c>sips -g hasAlpha</c> answers "no" for
    /// these files): the image is opaque, and its colors are those of heif-dec.
    /// </summary>
    [Theory]
    [InlineData("lossless", new[] { "-L" }, HeifDecComparison.Exact)]
    [InlineData("lossy-444", new[] { "-q", "70", "-p", "chroma=444" }, HeifDecComparison.Matrix)]
    public async Task AlphaPlanesOfTheTilesOfAGridAreNotTheAlphaPlaneOfTheGrid(string label, string[] options, HeifDecComparison comparison)
    {
        var heif = await RequiredTools.LibHeifAsync();
        var name = "grid-tile-alpha-" + label;
        var (path, source) = await EncodeSourceAsync(heif, name, SourceKind.Rgba8, 200, 136, [.. options, "--cut-tiles", "64"]);
        var reader = ReferenceHeif.Parse(await File.ReadAllBytesAsync(path, XunitCancellationToken));
        var grid = reader.PrimaryItem!;
        Assert.Equal("grid", grid.Type);
        Assert.Null(reader.AlphaItem);
        Assert.All(reader.GetGridTiles(grid), tile => Assert.NotNull(reader.FindAuxiliary(tile.Id, ReferenceHeif.HevcAlphaAuxiliaryType)));

        var still = await AssertStillAsync(heif, path, name, comparison, heifDecAlpha: HeifDecAlpha.FromTiles);
        using var image = Image.Load(path);
        Assert.Equal(PixelFormat.Rgb24, image.PixelFormat);
        Assert.Equal<bool?>(false, Image.Identify(path).MayHaveTransparency);
        if (comparison is HeifDecComparison.Exact)
        {
            // The colors of the source, the transparent pixels included
            AssertPixels(WithoutAlpha(ToRgba16(source)), still.Pixels, ColorBound.Exact, $"{name}: library vs the colors of the source");
        }
    }

    /// <summary>
    /// An alpha plane whose bit depth is not the one of the color image: the alpha item of a file of heif-enc is replaced,
    /// with <see cref="HeifFileBuilder"/>, by the alpha item of another encoding of the same source. The alpha samples are
    /// scaled to the precision of the pixels, which is the one of the color image: <c>round(a * max / alphaMax)</c>.
    /// </summary>
    [Theory]
    [InlineData(10, 8)]
    [InlineData(12, 8)]
    [InlineData(8, 10)]
    [InlineData(8, 12)]
    [InlineData(10, 12)]
    public async Task AlphaPlaneOfAnotherBitDepthIsScaledToThePrecisionOfThePixels(int colorDepth, int alphaDepth)
    {
        var heif = await RequiredTools.LibHeifAsync();
        var name = string.Create(CultureInfo.InvariantCulture, $"alpha-depth-{colorDepth}-{alphaDepth}");
        var (colorPath, _) = await EncodeSourceAsync(heif, name + "-color", colorDepth > 8 ? SourceKind.Rgba16 : SourceKind.Rgba8, 96, 64, DepthOptions(colorDepth), seed: 47);
        var (alphaPath, _) = await EncodeSourceAsync(heif, name + "-alpha", alphaDepth > 8 ? SourceKind.Rgba16 : SourceKind.Rgba8, 96, 64, DepthOptions(alphaDepth), seed: 47);
        var colorBytes = await File.ReadAllBytesAsync(colorPath, XunitCancellationToken);
        var donor = ReferenceHeif.Parse(await File.ReadAllBytesAsync(alphaPath, XunitCancellationToken));
        var donorAlpha = donor.AlphaItem!;
        var builder = HeifFileBuilder.From(colorBytes);
        var alphaItem = builder.GetItem(ReferenceHeif.Parse(colorBytes).AlphaItem!.Id);
        alphaItem.Data = donor.ReadItemData(donorAlpha);
        ReplaceProperty(builder, alphaItem, "hvcC", HeifFileBuilder.Box("hvcC", donorAlpha.Find("hvcC")!.Payload.ToArray()));
        ReplaceProperty(builder, alphaItem, "pixi", HeifFileBuilder.Box("pixi", donorAlpha.Find("pixi")!.Payload.ToArray()));
        var path = ArtifactPath(name + ".heic");
        await File.WriteAllBytesAsync(path, builder.Build(), XunitCancellationToken);

        var still = await AssertStillAsync(heif, path, name, HeifDecComparison.Exact, compareWithHeifDec: false);
        Assert.Equal<(int, int?)>((colorDepth, alphaDepth), (still.Color.BitDepth, still.Alpha?.BitDepth));

        static string[] DepthOptions(int depth) => depth > 8 ? ["-L", "-b", depth.ToString(CultureInfo.InvariantCulture)] : ["-L"];
    }

    /// <summary>
    /// A file with several images, which heif-enc writes when it is given several sources: the image of the file is its
    /// primary item, whichever it is (the primary item is changed with <see cref="HeifFileBuilder"/>), with the alpha plane
    /// that refers to it.
    /// </summary>
    [Fact]
    public async Task FileWithSeveralImagesPresentsItsPrimaryItem()
    {
        var heif = await RequiredTools.LibHeifAsync();
        const string Name = "several-images";
        var first = await SaveSourceAsync(Name + "-1", CreateSource(SourceKind.Rgb8, 96, 64, seed: 53));
        var second = await SaveSourceAsync(Name + "-2", CreateSource(SourceKind.Rgba8, 80, 72, seed: 59));
        var path = ArtifactPath(Name + ".heic");
        await heif.EncodeAsync(["-q", "70", "-p", "chroma=444", first, second], path, XunitCancellationToken);

        // heif-dec writes every image of the file, under other names: its pixels are not compared
        var still = await AssertStillAsync(heif, path, Name, HeifDecComparison.Matrix, compareWithHeifDec: false);
        Assert.Equal((96, 64, false), (still.Width, still.Height, still.Alpha is not null));

        var bytes = await File.ReadAllBytesAsync(path, XunitCancellationToken);
        var reader = ReferenceHeif.Parse(bytes);
        var other = reader.Items.Single(item => item.Type == "hvc1" && item.Id != reader.PrimaryItemId && item.AuxiliaryType is null);
        var builder = HeifFileBuilder.From(bytes);
        builder.PrimaryItemId = other.Id;
        var otherPath = ArtifactPath(Name + "-other-primary.heic");
        await File.WriteAllBytesAsync(otherPath, builder.Build(), XunitCancellationToken);
        var otherStill = await AssertStillAsync(heif, otherPath, Name + "-other-primary", HeifDecComparison.Matrix, compareWithHeifDec: false);
        Assert.Equal((80, 72, true), (otherStill.Width, otherStill.Height, otherStill.Alpha is not null));
    }

    /// <summary>
    /// Files that libheif writes or reads and that the library documents as unsupported: loading them throws
    /// <see cref="UnsupportedImageFeatureException"/>; they are never decoded approximately.
    /// </summary>
    [Theory]
    [InlineData("premultiplied-alpha", SourceKind.Rgba8, new[] { "-q", "60", "--premultiplied-alpha" })]
    [InlineData("chroma-location-3", SourceKind.Rgb8, new[] { "-q", "60", "-p", "x265:chromaloc=3" })]
    [InlineData("chroma-location-4", SourceKind.Rgb8, new[] { "-q", "60", "-p", "x265:chromaloc=4" })]
    [InlineData("chroma-location-5", SourceKind.Rgb8, new[] { "-q", "60", "-p", "x265:chromaloc=5" })]
    public async Task FilesDocumentedAsUnsupportedAreRejected(string label, SourceKind kind, string[] options)
    {
        var heif = await RequiredTools.LibHeifAsync();
        var (path, _) = await EncodeSourceAsync(heif, "unsupported-" + label, kind, 80, 64, options);

        // The file is valid for the reference decoder
        var reference = await heif.DecodeAsync(path, bilinearChroma: true, XunitCancellationToken);
        Assert.Equal((80, 64), (reference.Width, reference.Height));

        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(path).Dispose());
        Assert.Equal(ImageFormat.Heic, exception.Format);
    }

    /// <summary>
    /// Files that are not valid: loading them throws <see cref="InvalidImageContentException"/>. heif-dec rejects them
    /// too, except a video track, which is not an image sequence.
    /// </summary>
    [Theory]
    [InlineData("identity-matrix-420", false)]
    [InlineData("truncated-item-data", false)]
    [InlineData("item-data-outside-file", false)]
    [InlineData("missing-tile", false)]
    [InlineData("wrong-image-size", false)]
    [InlineData("missing-configuration", false)]
    [InlineData("video-track", true)]
    public async Task InvalidFilesAreRejected(string label, bool heifDecDecodes)
    {
        var heif = await RequiredTools.LibHeifAsync();
        var name = "invalid-" + label;
        var path = ArtifactPath(name + ".heic");
        switch (label)
        {
            case "identity-matrix-420":
                // heif-enc codes 4:2:0 planes and declares the identity matrix, which only applies to 4:4:4 planes
                await EncodeSourceAsync(heif, name, SourceKind.Rgb8, 80, 64, ["-q", "60", "--matrix_coefficients", "0"]);
                break;

            case "truncated-item-data":
                {
                    var (source, _) = await EncodeSourceAsync(heif, name + "-source", SourceKind.Rgb8, 80, 64, ["-q", "90"]);
                    var bytes = await File.ReadAllBytesAsync(source, XunitCancellationToken);
                    await File.WriteAllBytesAsync(path, bytes[..^(bytes.Length / 4)], XunitCancellationToken);
                    break;
                }

            case "item-data-outside-file":
                {
                    // The extent of the image starts in the file and ends after it
                    var builder = await ImportAsync(heif, name, ["-q", "90"]);
                    var item = builder.Items.Single(item => item.Type == "hvc1");
                    var length = builder.Build().Length;
                    item.ExtentOverride = [(length - 100, item.Data.Length)];
                    await File.WriteAllBytesAsync(path, builder.Build(), XunitCancellationToken);
                    break;
                }

            case "missing-tile":
                {
                    // A tile of the grid is an item that does not exist
                    var builder = await ImportAsync(heif, name, ["-L", "--cut-tiles", "64"], width: 128, height: 128);
                    var tiles = builder.References.Single(reference => reference.Type == "dimg");
                    builder.Items.Remove(builder.GetItem(tiles.ToItemIds[^1]));
                    await File.WriteAllBytesAsync(path, builder.Build(), XunitCancellationToken);
                    break;
                }

            case "wrong-image-size":
                {
                    // The 'ispe' property declares 64x64 samples; the picture has 80x64
                    var builder = await ImportAsync(heif, name, ["-L"]);
                    var item = builder.Items.Single(item => item.Type == "hvc1");
                    ReplaceProperty(builder, item, "ispe", HeifFileBuilder.ImageSize(64, 64));
                    await File.WriteAllBytesAsync(path, builder.Build(), XunitCancellationToken);
                    break;
                }

            case "missing-configuration":
                {
                    var builder = await ImportAsync(heif, name, ["-L"]);
                    RemoveProperties(builder, "hvcC");
                    await File.WriteAllBytesAsync(path, builder.Build(), XunitCancellationToken);
                    break;
                }

            case "video-track":
                {
                    // "-V" writes a track of handler type "vide" and no image item: a video, under the brands of an image sequence
                    var directory = ArtifactPath(name + ".frames");
                    Directory.CreateDirectory(directory);
                    FullPath first = default;
                    for (var i = 0; i < 3; i++)
                    {
                        var frame = await SaveSourceAsync(string.Create(CultureInfo.InvariantCulture, $"frame-{i + 1:D3}"), CreateSource(SourceKind.Rgb8, 80, 64, seed: 37, frame: i), directory);
                        first = i == 0 ? frame : first;
                    }

                    await heif.EncodeAsync(["-S", "-V", "-q", "60", "--gop-structure", "intra-only", first], path, XunitCancellationToken);
                    break;
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(label), label, "Unknown case.");
        }

        var result = label == "video-track"
            ? await DecodeVideoAsync()
            : await heif.TryDecodeAsync(path, bilinearChroma: true, XunitCancellationToken);
        Assert.Equal(heifDecDecodes, result.Image is not null, message: $"{name}: heif-dec: {result.Error ?? "decodes the file"}.");
        WriteOutput($"{name}: heif-dec {heif.Version}: {result.Error ?? "decodes the file"}");

        var exception = Assert.Throws<InvalidImageContentException>(() => Image.Load(path).Dispose());
        Assert.Equal(ImageFormat.Heic, exception.Format);
        WriteOutput($"{name}: library: {exception.Message}");

        async Task<(HeifDecImage? Image, string? Error)> DecodeVideoAsync()
        {
            var frames = await heif.DecodeSequenceAsync(path, bilinearChroma: true, XunitCancellationToken);
            return (frames[0].Image, null);
        }
    }

    /// <summary>Encodes a source with heif-enc and imports the file in a builder.</summary>
    private static async Task<HeifFileBuilder> ImportAsync(LibHeifTool heif, string name, string[] options, int width = 80, int height = 64)
    {
        var (path, _) = await EncodeSourceAsync(heif, name + "-source", SourceKind.Rgb8, width, height, options);
        return HeifFileBuilder.From(await File.ReadAllBytesAsync(path, XunitCancellationToken));
    }

    /// <summary>The four-character code of a property of a builder, by its 1-based index.</summary>
    private static string GetPropertyType(HeifFileBuilder builder, int index) => Encoding.Latin1.GetString(builder.Properties[index - 1], 4, 4);

    /// <summary>Removes the associations of every item with the properties of a type; the properties stay in the file, unused.</summary>
    private static void RemoveProperties(HeifFileBuilder builder, string type)
    {
        var removed = 0;
        foreach (var item in builder.Items)
        {
            for (var i = item.Associations.Count - 1; i >= 0; i--)
            {
                if (GetPropertyType(builder, item.Associations[i].PropertyIndex) == type)
                {
                    item.Associations.RemoveAt(i);
                    removed++;
                }
            }
        }

        Assert.True(removed > 0, $"No item has a '{type}' property.");
    }

    /// <summary>Replaces the property of a type of an item by another box.</summary>
    private static void ReplaceProperty(HeifFileBuilder builder, HeifFileBuilder.Item item, string type, byte[] box)
    {
        var index = item.Associations.Select((association, position) => (association, position)).Single(entry => GetPropertyType(builder, entry.association.PropertyIndex) == type).position;
        item.Associations[index] = (builder.AddProperty(box), item.Associations[index].IsEssential);
    }
}
