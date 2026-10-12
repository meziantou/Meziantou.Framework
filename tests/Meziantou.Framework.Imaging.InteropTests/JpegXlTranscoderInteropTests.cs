using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// <see cref="JpegXlTranscoder"/> against the reference tools, in both directions and on bytes:
/// <list type="bullet">
/// <item><description>a JPEG file recompressed by the library is rebuilt, byte for byte, by libjxl's <c>djxl</c> and by jxl-oxide, and decodes to the pixels the reference decoders show;</description></item>
/// <item><description>a JPEG file recompressed by <c>cjxl</c>, with and without chroma from luma, is rebuilt byte for byte by the library;</description></item>
/// <item><description>the files the library refuses to recompress are files the reference encoder refuses too.</description></item>
/// </list>
/// The JPEG files are those of the corpus (written by libjpeg-turbo and FFmpeg) and files the JPEG encoder of the library writes.
/// </summary>
public sealed class JpegXlTranscoderInteropTests
{
    public static TheoryData<string> JpegFixtures => [.. GoldenCorpus.Default.GetIds(format: "jpeg", kind: FixtureKinds.Valid)];

    /// <summary>
    /// A sampling factor of 4, a restart interval that changes between scans, arithmetic coding or samples of 12 bits: a
    /// JPEG XL file cannot describe them.
    /// </summary>
    private static bool IsRefusedByTheLibrary(string id)
        => id is "jpeg/baseline-411-odd" or "jpeg/progressive-411-separate-dc" or "jpeg/progressive-420-odd-restart-rows-deep"
            || id.Contains("arithmetic", StringComparison.Ordinal) || id.Contains("precision12", StringComparison.Ordinal);

    /// <summary>cjxl 0.12.0 also refuses a file whose metadata it cannot place in the reconstruction data; the library recompresses it.</summary>
    private static bool IsRefusedByCjxl(string id) => IsRefusedByTheLibrary(id) || id is "jpeg/metadata-icc-exif-dpcm";

    [Theory]
    [MemberData(nameof(JpegFixtures))]
    public async Task AFileRecompressedByTheLibraryIsRebuiltByTheReferenceDecoders(string id)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var oxide = await RequiredTools.JxlOxideAsync();
        var fixture = GoldenCorpus.Default.Get(id);
        var jpeg = fixture.ReadInput();
        var directory = InteropSettings.GetArtifactsDirectory(nameof(AFileRecompressedByTheLibraryIsRebuiltByTheReferenceDecoders));
        var name = id.Replace('/', '-');
        var path = directory / (name + ".jxl");

        // Every effort is exercised across the corpus; the effort of a case only depends on its name
        var options = new JpegXlTranscodeOptions { Effort = id.Sum(c => c) % 10 };
        if (IsRefusedByTheLibrary(id))
        {
            Assert.Throws<UnsupportedImageFeatureException>(() => JpegXlTranscoder.RecompressJpeg(fixture.InputPath, path, options));
            Assert.False(File.Exists(path));

            // What the library refuses is not a file it fails to describe: the reference encoder refuses it too
            await Assert.ThrowsAsync<InvalidOperationException>(() => jxl.EncodeAsync(fixture.InputPath, directory / (name + ".cjxl.jxl"), ["--lossless_jpeg=1"], XunitCancellationToken));
            return;
        }

        File.Delete(path);
        await JpegXlTranscoder.RecompressJpegAsync(fixture.InputPath, path, options, XunitCancellationToken);
        var info = await jxl.GetInfoAsync(path, XunitCancellationToken);
        Assert.True(info.HasJpegReconstruction);
        Assert.True(info.IsContainer);
        Assert.False(info.IsXybEncoded);

        var rebuilt = await jxl.ReconstructJpegAsync(path, XunitCancellationToken);
        Assert.True(jpeg.AsSpan().SequenceEqual(rebuilt), $"{id}: djxl {jxl.Version} rebuilds {rebuilt.Length} bytes that are not the {jpeg.Length} bytes of the JPEG file.");

        // jxl-oxide 0.12.6 does not rebuild every JPEG file. When it fails on the file of the library, it must fail in the
        // same way on the file cjxl makes from the same JPEG file: the limit is then its own
        var oxideRebuilt = await oxide.TryReconstructJpegAsync(path, XunitCancellationToken);
        if (oxideRebuilt is null || !jpeg.AsSpan().SequenceEqual(oxideRebuilt))
        {
            Assert.False(IsRefusedByCjxl(id), $"{id}: jxl-oxide {oxide.Version} does not rebuild the file, and there is no file of cjxl to compare with.");
            var referencePath = directory / (name + ".cjxl.jxl");
            await jxl.EncodeAsync(fixture.InputPath, referencePath, ["--lossless_jpeg=1"], XunitCancellationToken);
            var oxideReference = await oxide.TryReconstructJpegAsync(referencePath, XunitCancellationToken);
            Assert.True(oxideReference is null || !jpeg.AsSpan().SequenceEqual(oxideReference), $"{id}: jxl-oxide {oxide.Version} rebuilds the file of cjxl but not the file of the library.");
        }
    }

    /// <summary>
    /// The reference encoder chooses what the library does not: chroma from luma for files without subsampling, and its
    /// own layout of the reconstruction data at every effort.
    /// </summary>
    [Theory]
    [MemberData(nameof(JpegFixtures))]
    public async Task AFileRecompressedByCjxlIsRebuiltByTheLibrary(string id)
    {
        if (IsRefusedByCjxl(id))
            return;

        var jxl = await RequiredTools.LibJxlAsync();
        var fixture = GoldenCorpus.Default.Get(id);
        var jpeg = fixture.ReadInput();
        var directory = InteropSettings.GetArtifactsDirectory(nameof(AFileRecompressedByCjxlIsRebuiltByTheLibrary));
        var count = 0;
        foreach (var arguments in new[] { "--lossless_jpeg=1", "--lossless_jpeg=1 --jpeg_reconstruction_cfl=0", "--lossless_jpeg=1 -e 3", "--lossless_jpeg=1 -e 9" })
        {
            var path = directory / $"{id.Replace('/', '-')}-{count++}.jxl";
            await jxl.EncodeAsync(fixture.InputPath, path, arguments.Split(' '), XunitCancellationToken);
            var data = await File.ReadAllBytesAsync(path, XunitCancellationToken);
            Assert.True(JpegXlTranscoder.HasJpegReconstructionData(data));
            using var rebuilt = new MemoryStream();
            JpegXlTranscoder.ReconstructJpeg(data, rebuilt);
            Assert.True(jpeg.AsSpan().SequenceEqual(rebuilt.GetBuffer().AsSpan(0, (int)rebuilt.Length)), $"{id}: the library rebuilds {rebuilt.Length} bytes that are not the {jpeg.Length} bytes of the JPEG file from the output of cjxl {jxl.Version} {arguments}.");
        }
    }

    /// <summary>
    /// Files of the JPEG encoder of the library, baseline and progressive, through both chains: recompressed by the library and rebuilt by djxl,
    /// recompressed by cjxl and rebuilt by the library. The recompressed file of the library also decodes to the pixels
    /// the reference decoders show.
    /// </summary>
    [Theory]
    [InlineData("photo-rgb8", JpegChromaSubsampling.Ratio444, 85, JpegEncodingProcess.Baseline)]
    [InlineData("photo-rgb8", JpegChromaSubsampling.Ratio422, 85, JpegEncodingProcess.Baseline)]
    [InlineData("photo-rgb8", JpegChromaSubsampling.Ratio420, 85, JpegEncodingProcess.Baseline)]
    [InlineData("photo-rgb8", JpegChromaSubsampling.Ratio444, 100, JpegEncodingProcess.Baseline)]
    [InlineData("photo-rgb8", JpegChromaSubsampling.Ratio420, 20, JpegEncodingProcess.Baseline)]
    [InlineData("photo-gray8", JpegChromaSubsampling.Auto, 85, JpegEncodingProcess.Baseline)]
    [InlineData("large-rgb8", JpegChromaSubsampling.Ratio420, 75, JpegEncodingProcess.Baseline)]
    [InlineData("text-rgb8", JpegChromaSubsampling.Ratio444, 90, JpegEncodingProcess.Baseline)]
    [InlineData("smooth-rgb8", JpegChromaSubsampling.Ratio422, 50, JpegEncodingProcess.Baseline)]
    [InlineData("smooth-gray8", JpegChromaSubsampling.Auto, 95, JpegEncodingProcess.Baseline)]
    [InlineData("photo-rgb8", JpegChromaSubsampling.Ratio444, 85, JpegEncodingProcess.Progressive)]
    [InlineData("photo-rgb8", JpegChromaSubsampling.Ratio420, 60, JpegEncodingProcess.Progressive)]
    [InlineData("photo-gray8", JpegChromaSubsampling.Auto, 90, JpegEncodingProcess.Progressive)]
    [InlineData("text-rgb8", JpegChromaSubsampling.Ratio422, 75, JpegEncodingProcess.Progressive)]
    public async Task FilesOfTheJpegEncoderGoThroughBothChains(string patternName, JpegChromaSubsampling subsampling, int quality, JpegEncodingProcess process)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var pattern = JpegXlPattern.Get(patternName);
        var directory = InteropSettings.GetArtifactsDirectory(nameof(FilesOfTheJpegEncoderGoThroughBothChains));
        var name = $"{patternName}-{subsampling}-q{quality}-{process}";
        var jpegPath = directory / (name + ".jpg");
        var libraryPath = directory / (name + ".library.jxl");
        var referencePath = directory / (name + ".cjxl.jxl");
        using (var image = CreateImage(pattern))
        {
            await image.SaveAsync(jpegPath, new JpegEncoder { Quality = quality, ChromaSubsampling = subsampling, Process = process, AllowBitDepthReduction = true }, XunitCancellationToken);
        }

        var jpeg = await File.ReadAllBytesAsync(jpegPath, XunitCancellationToken);

        File.Delete(libraryPath);
        await JpegXlTranscoder.RecompressJpegAsync(jpegPath, libraryPath, cancellationToken: XunitCancellationToken);
        Assert.True((await jxl.GetInfoAsync(libraryPath, XunitCancellationToken)).HasJpegReconstruction);
        var rebuiltByDjxl = await jxl.ReconstructJpegAsync(libraryPath, XunitCancellationToken);
        Assert.True(jpeg.AsSpan().SequenceEqual(rebuiltByDjxl), $"{name}: djxl {jxl.Version} rebuilds {rebuiltByDjxl.Length} bytes that are not the {jpeg.Length} bytes of the JPEG file.");

        // Smaller than the JPEG file: the point of the recompression. Not for an image of a few thousand samples, where the
        // reconstruction data (the Huffman tables of the JPEG file, 250 bytes) outweigh the gain, for cjxl as well
        if (pattern.Width * pattern.Height >= 256 * 256)
        {
            Assert.True(new FileInfo(libraryPath).Length < jpeg.Length, $"{name}: the recompressed file has {new FileInfo(libraryPath).Length} bytes, the JPEG file {jpeg.Length}.");
        }

        // The pixels of the recompressed file, which stores the coefficients without chroma from luma
        using (await JpegXlReferenceComparison.AssertDecodesLikeTheReferencesAsync(libraryPath, JpegXlReferenceComparison.DjxlTolerance, JpegXlReferenceComparison.OxideTolerance, name))
        {
        }

        await jxl.EncodeAsync(jpegPath, referencePath, ["--lossless_jpeg=1"], XunitCancellationToken);
        using var rebuilt = new MemoryStream();
        await using (var reference = File.OpenRead(referencePath))
        {
            Assert.True(await JpegXlTranscoder.TryReconstructJpegAsync(reference, rebuilt, cancellationToken: XunitCancellationToken));
        }

        Assert.True(jpeg.AsSpan().SequenceEqual(rebuilt.GetBuffer().AsSpan(0, (int)rebuilt.Length)), $"{name}: the library does not rebuild the JPEG file from the output of cjxl {jxl.Version}.");
    }

    private static Image CreateImage(JpegXlPattern pattern)
    {
        Assert.Equal(255, pattern.MaxValue);
        var bytes = new byte[pattern.Samples.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)pattern.Samples[i];
        }

        return pattern.Channels switch
        {
            1 => Image.ImportPixelBytes<Gray8>(bytes, pattern.Width, pattern.Height),
            3 => Image.ImportPixelBytes<Rgb24>(bytes, pattern.Width, pattern.Height),
            _ => throw new ArgumentException("The pattern has no pixel format a JPEG file stores.", nameof(pattern)),
        };
    }
}
