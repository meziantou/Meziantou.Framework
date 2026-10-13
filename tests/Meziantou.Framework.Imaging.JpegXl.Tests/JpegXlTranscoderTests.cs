using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Streams;
using Meziantou.Framework.Imaging.Tests.Codecs;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// <see cref="JpegXlTranscoder"/>: a JPEG file recompressed as a JPEG XL file, and the JPEG file rebuilt from it. The
/// assertions are on bytes: a rebuilt file is the original file, never a file that decodes to similar pixels. The inputs
/// are the JPEG files of the corpus (written by libjpeg-turbo and FFmpeg), the files of the corpus that cjxl made from
/// some of them, and files the test JPEG writer builds with the scan scripts, restart intervals, Huffman tables and
/// segments an encoder may choose. djxl and jxl-oxide rebuild the files the library writes in the interop job.
/// </summary>
public sealed class JpegXlTranscoderTests
{
    public static TheoryData<string> JpegFixtures => [.. GoldenCorpus.Default.GetIds(format: "jpeg", kind: FixtureKinds.Valid)];

    public static TheoryData<string> InvalidJpegFixtures => [.. GoldenCorpus.Default.GetIds(format: "jpeg", kind: FixtureKinds.Invalid)];

    public static TheoryData<string> UnsupportedJpegFixtures => [.. GoldenCorpus.Default.GetIds(format: "jpeg", kind: FixtureKinds.Unsupported)];

    [Theory]
    [InlineData("jxl/jpeg-420", "jpeg/ffmpeg-mjpeg-420-37x23")]
    [InlineData("jxl/jpeg-422", "jpeg/baseline-422-odd-restart")]
    [InlineData("jxl/jpeg-444", "jpeg/baseline-444-detail")]
    [InlineData("jxl/jpeg-gray", "jpeg/progressive-gray-odd-refinement")]
    public void AFileWrittenByTheReferenceEncoderGivesBackItsJpegFile(string id, string sourceId)
    {
        var data = GoldenCorpus.Default.Get(id).ReadInput();
        var expected = GoldenCorpus.Default.Get(sourceId).ReadInput();
        Assert.True(JpegXlTranscoder.HasJpegReconstructionData(data));
        using var output = new MemoryStream();
        JpegXlTranscoder.ReconstructJpeg(data, output);
        Assert.Equal(expected, output.ToArray());
    }

    /// <summary>
    /// What a decoder reads and the encoders of the corpus do not write: bytes after the last block of a scan, a
    /// quantization table defined between two scans (before the scan of its component, again with the same values, or in
    /// the slot of another one), a table that repeats the one before it and that no component uses. A table that no
    /// component uses and that is not such a copy has no place in a JPEG XL file: the file is refused, and nothing is written.
    /// </summary>
    [Fact]
    public void FilesWithMovedAndRepeatedSegmentsAreRebuiltOrRefused()
    {
        var variants = JpegTestVariants.Create(GoldenCorpus.Default.Get("jpeg/baseline-420").ReadInput(), GoldenCorpus.Default.Get("jpeg/multiscan-chroma-interleaved").ReadInput());
        Assert.Contains(variants, variant => variant.IsRecompressed);
        Assert.Contains(variants, variant => !variant.IsRecompressed);
        foreach (var variant in variants)
        {
            // Every variant is a file the decoder reads: what is refused is refused as a file a JPEG XL file cannot hold
            using var loaded = Image.Load<Rgba32>(variant.File);
            using var output = new MemoryStream();
            if (!variant.IsRecompressed)
            {
                var exception = Assert.Throws<UnsupportedImageFeatureException>(() => JpegXlTranscoder.RecompressJpeg(variant.File, output));
                Assert.Equal(ImageFormat.Jpeg, exception.Format);
                Assert.Equal(0, output.Length);
                continue;
            }

            AssertRoundTrips(variant.File, variant.Name);

            // The pixels of the JPEG XL file are the ones of the JPEG file, to the rounding of the two decoders
            JpegXlTranscoder.RecompressJpeg(variant.File, output);
            using var recompressed = Image.Load<Rgba32>(output.ToArray());
            Assert.True(GetMaxDifference(loaded, recompressed) <= 48, $"{variant.Name}: the pixels of the recompressed file differ by {GetMaxDifference(loaded, recompressed)}.");
        }
    }

    /// <summary>The file of the corpus whose reconstruction data follow the codestream gives back the JPEG file it was made from.</summary>
    [Fact]
    public void AFileWhoseReconstructionDataFollowTheCodestreamGivesBackItsJpegFile()
    {
        var data = GoldenCorpus.Default.Get("jxl/jpeg-420-reconstruction-after").ReadInput();
        var expected = GoldenCorpus.Default.Get("jpeg/ffmpeg-mjpeg-420-37x23").ReadInput();

        // The reconstruction data are the last box of the file: 124 bytes
        Assert.Equal(124u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(^124..)));
        Assert.Equal("jbrd"u8, data.AsSpan(^120..^116));
        Assert.True(JpegXlTranscoder.HasJpegReconstructionData(data));
        using var output = new MemoryStream();
        JpegXlTranscoder.ReconstructJpeg(data, output);
        Assert.Equal(expected, output.ToArray());
    }

    [Theory]
    [MemberData(nameof(JpegFixtures))]
    public void AJpegFileIsRecompressedAndRebuilt(string id)
    {
        var jpeg = GoldenCorpus.Default.Get(id).ReadInput();
        using var recompressed = new MemoryStream();

        // What a JPEG XL frame cannot hold is refused, never approximated: a sampling factor of 4, a file that changes its
        // restart interval between scans, arithmetic coding and samples of 12 bits
        if (id is "jpeg/baseline-411-odd" or "jpeg/progressive-411-separate-dc" or "jpeg/progressive-420-odd-restart-rows-deep"
            || id.Contains("arithmetic", StringComparison.Ordinal) || id.Contains("precision12", StringComparison.Ordinal))
        {
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => JpegXlTranscoder.RecompressJpeg(jpeg, recompressed));
            Assert.Equal(ImageFormat.Jpeg, exception.Format);
            Assert.Equal(0, recompressed.Length);
            return;
        }

        JpegXlTranscoder.RecompressJpeg(jpeg, recompressed);
        var data = recompressed.ToArray();
        Assert.True(JpegXlTranscoder.HasJpegReconstructionData(data));
        using var rebuilt = new MemoryStream();
        Assert.True(JpegXlTranscoder.TryReconstructJpeg(data, rebuilt));
        Assert.Equal(jpeg, rebuilt.ToArray());

        // The JPEG XL file is an image like any other: the same size, the same metadata, and the same picture. The two
        // formats do not reconstruct the samples in the same way (the chroma is not upsampled with the same filter, and
        // JPEG XL moves a quantized value towards zero), so the pixels are only required to be those of the same picture:
        // djxl decodes these files in the interop job, and the decoder of the library is compared with it there
        using var expected = Image.Load(jpeg);
        using var actual = Image.Load(data);
        Assert.Equal((expected.Width, expected.Height, expected.PixelFormat), (actual.Width, actual.Height, actual.PixelFormat));
        Assert.Equal(expected.Metadata.Orientation, actual.Metadata.Orientation);
        Assert.Equal(expected.Metadata.IccProfile?.Data.ToArray(), actual.Metadata.IccProfile?.Data.ToArray());
        Assert.Equal(expected.Metadata.XmpProfile?.Data.ToArray(), actual.Metadata.XmpProfile?.Data.ToArray());
        var discardProfile = new PixelConversionOptions { DiscardIncompatibleColorProfile = true };
        using var expectedPixels = expected.CloneAs<Rgba32>(discardProfile);
        using var actualPixels = actual.CloneAs<Rgba32>(discardProfile);
        var maxDifference = GetMaxDifference(expectedPixels, actualPixels);
        Assert.True(maxDifference <= 48, $"{id}: the pixels differ by {maxDifference}.");
    }

    [Theory]
    [MemberData(nameof(InvalidJpegFixtures))]
    [MemberData(nameof(UnsupportedJpegFixtures))]
    public void AJpegFileThatDoesNotLoadIsRefusedLikeALoad(string id)
    {
        var jpeg = GoldenCorpus.Default.Get(id).ReadInput();
        Exception? loadException = null;
        try
        {
            using var image = Image.Load(jpeg);
        }
        catch (ImageException exception)
        {
            loadException = exception;
        }

        // A file with four components loads once converted, and is not recompressed: the others fail in the same way
        using var output = new MemoryStream();
        var recompressException = Assert.ThrowsAny<ImageException>(() => JpegXlTranscoder.RecompressJpeg(jpeg, output));
        Assert.Equal(0, output.Length);
        if (loadException is not null)
        {
            Assert.Equal(loadException.GetType(), recompressException.GetType());
        }
    }

    public static TheoryData<string, int, int, bool> SyntheticCases()
    {
        var data = new TheoryData<string, int, int, bool>();
        foreach (var (width, height) in new[] { (8, 8), (13, 9), (33, 17), (300, 270) })
        {
            foreach (var sampling in new[] { "gray", "444", "420", "422", "440", "all-1x2" })
            {
                data.Add(sampling, width, height, false);
                data.Add(sampling, width, height, true);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SyntheticCases))]
    public void EveryChoiceOfAnEncoderIsRebuilt(string sampling, int width, int height, bool progressive)
    {
        (int H, int V)[] factors = sampling switch
        {
            "gray" => [(1, 1)],
            "444" => [(1, 1), (1, 1), (1, 1)],
            "420" => [(2, 2), (1, 1), (1, 1)],
            "422" => [(2, 1), (1, 1), (1, 1)],
            "440" => [(1, 2), (1, 1), (1, 1)],
            _ => [(1, 2), (1, 2), (1, 2)],
        };
        var image = JpegTestImage.CreateRandom(width, height, factors, seed: width + height + factors.Length);
        var all = Enumerable.Range(0, factors.Length).ToArray();
        var comment = JpegTestImage.Segment(0xFE, "a comment"u8);
        var application = JpegTestImage.Segment(0xE5, [1, 2, 3, 0xFF, 0x00, 0xD9]);
        JpegTestEncodeOptions[] options = progressive
            ?
            [
                new JpegTestEncodeOptions { Progression = JpegTestProgressiveScan.ParseScript(GetScript(all, deep: false)) },
                new JpegTestEncodeOptions { Progression = JpegTestProgressiveScan.ParseScript(GetScript(all, deep: true)), RestartInterval = 3, Huffman = JpegTestHuffmanStyle.Skewed },

                // Runs of blocks that an encoder would join: the file says where they stop
                new JpegTestEncodeOptions { Progression = JpegTestProgressiveScan.ParseScript(GetScript(all, deep: true)), MaxEndOfBandRun = 1, SegmentsBeforeFrame = [comment] },
                new JpegTestEncodeOptions { Progression = JpegTestProgressiveScan.ParseScript(GetScript(all, deep: true)), MaxEndOfBandRun = 3, TablesBeforeEachScan = true, HuffmanSeed = 5 },

                // Runs of zeros before the end of a band, which an encoder would leave to the end of the band
                new JpegTestEncodeOptions { Progression = JpegTestProgressiveScan.ParseScript(GetScript(all, deep: true)), MaxEndOfBandRun = 2, ZeroRunsBeforeEndOfBlock = 2 },
            ]
            :
            [
                new JpegTestEncodeOptions(),
                new JpegTestEncodeOptions { Huffman = JpegTestHuffmanStyle.Skewed, RestartInterval = 1 },
                new JpegTestEncodeOptions { FrameMarker = 0xC1, RestartInterval = 3, HuffmanSeed = 7, SegmentsBeforeFrame = [application, comment], SegmentsAfterScans = [comment] },
                new JpegTestEncodeOptions { Scans = [.. all.Reverse().Select(index => new[] { index })], TablesBeforeEachScan = true, RestartInterval = 2 },

                // Runs of zeros before the end of a block, which an encoder would leave to the end of the block
                new JpegTestEncodeOptions { Scans = [.. all.Select(index => new[] { index })], ZeroRunsBeforeEndOfBlock = 3 },
            ];

        for (var i = 0; i < options.Length; i++)
        {
            var jpeg = image.Encode(options[i]);
            AssertRoundTrips(jpeg, $"{sampling} {width}x{height} options {i}");

            // Bytes after the end of the image, and fill bytes before a marker, are part of the file
            byte[] withTail = [.. jpeg, 1, 2, 0xFF, 0xD9, 3];
            AssertRoundTrips(withTail, $"{sampling} {width}x{height} options {i} with a tail");
            byte[] withFill = [.. jpeg[..^2], 0xFF, 0xFF, 0xFF, .. jpeg[^2..]];
            AssertRoundTrips(withFill, $"{sampling} {width}x{height} options {i} with fill bytes before the end of image");
        }

        static string GetScript(int[] components, bool deep)
        {
            // The DC coefficients of every component, then the AC coefficients of each: in one scan, or in two bands with
            // their last bit in a scan of its own
            var list = string.Join(',', components);
            var scans = new List<string> { deep ? $"{list}: 0 0 0 1" : $"{list}: 0 0 0 0" };
            foreach (var component in components)
            {
                if (deep)
                {
                    scans.Add($"{component}: 1 5 0 2");
                    scans.Add($"{component}: 6 63 0 1");
                    scans.Add($"{component}: 1 5 2 1");
                    scans.Add($"{component}: 1 63 1 0");
                }
                else
                {
                    scans.Add($"{component}: 1 63 0 0");
                }
            }

            if (deep)
            {
                scans.Add($"{list}: 0 0 1 0");
            }

            return string.Join("; ", scans);
        }
    }

    [Fact]
    public void TheEffortDoesNotChangeTheRebuiltFile()
    {
        var jpeg = GoldenCorpus.Default.Get("jpeg/baseline-420").ReadInput();
        foreach (var effort in new[] { 0, 3, 9 })
        {
            using var recompressed = new MemoryStream();
            JpegXlTranscoder.RecompressJpeg(jpeg, recompressed, new JpegXlTranscodeOptions { Effort = effort });
            using var rebuilt = new MemoryStream();
            JpegXlTranscoder.ReconstructJpeg(recompressed.ToArray(), rebuilt);
            Assert.Equal(jpeg, rebuilt.ToArray());
        }

        Assert.Equal(5, JpegXlTranscodeOptions.Default.Effort);
        Assert.Same(ImageConfiguration.Default, JpegXlTranscodeOptions.Default.Configuration);
        Assert.Throws<ArgumentOutOfRangeException>(() => new JpegXlTranscodeOptions { Effort = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new JpegXlTranscodeOptions { Effort = 10 });
        Assert.Throws<ArgumentNullException>(() => new JpegXlTranscodeOptions { Configuration = null! });
    }

    [Fact]
    public void AFileWithoutReconstructionDataIsNotRebuilt()
    {
        // A JPEG XL file made from pixels: by the reference encoder, and by the encoder of the library
        using var image = new Image<Rgb24>(40, 24);
        using var encoded = new MemoryStream();
        image.Save(encoded, new JpegXlEncoder { Compression = JpegXlCompression.Lossy });
        foreach (var data in new[] { GoldenCorpus.Default.Get("jxl/lossy-rgb8").ReadInput(), GoldenCorpus.Default.Get("jxl/lossless-container").ReadInput(), encoded.ToArray() })
        {
            Assert.False(JpegXlTranscoder.HasJpegReconstructionData(data));
            using var output = new MemoryStream();
            Assert.False(JpegXlTranscoder.TryReconstructJpeg(data, output));
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => JpegXlTranscoder.ReconstructJpeg(data, output));
            Assert.Equal(ImageFormat.JpegXl, exception.Format);
            Assert.Equal(0, output.Length);
        }
    }

    [Fact]
    public void AnInputOfAnotherFormatIsRefused()
    {
        var jpeg = GoldenCorpus.Default.Get("jpeg/baseline-420").ReadInput();
        var jpegXl = GoldenCorpus.Default.Get("jxl/jpeg-420").ReadInput();
        using var output = new MemoryStream();
        Assert.Equal(ImageFormat.JpegXl, Assert.Throws<UnsupportedImageFeatureException>(() => JpegXlTranscoder.RecompressJpeg(jpegXl, output)).Format);
        Assert.Equal(ImageFormat.Jpeg, Assert.Throws<UnsupportedImageFeatureException>(() => JpegXlTranscoder.ReconstructJpeg(jpeg, output)).Format);
        Assert.Equal(ImageFormat.Jpeg, Assert.Throws<UnsupportedImageFeatureException>(() => JpegXlTranscoder.TryReconstructJpeg(jpeg, output)).Format);
        Assert.Equal(ImageFormat.Jpeg, Assert.Throws<UnsupportedImageFeatureException>(() => JpegXlTranscoder.HasJpegReconstructionData(jpeg)).Format);
        Assert.Throws<UnknownImageFormatException>(() => JpegXlTranscoder.RecompressJpeg("not an image"u8, output));
        Assert.Throws<UnknownImageFormatException>(() => JpegXlTranscoder.ReconstructJpeg("not an image"u8, output));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task StreamsAndPathsGiveTheSameFiles()
    {
        var jpeg = GoldenCorpus.Default.Get("jpeg/progressive-420-partial-mcu").ReadInput();
        using var expected = new MemoryStream();
        JpegXlTranscoder.RecompressJpeg(jpeg, expected);
        var jpegXl = expected.ToArray();

        // A stream that cannot seek and gives a few bytes at each read
        using (var input = CreateStream(jpeg, isAsynchronous: false))
        using (var output = new MemoryStream())
        {
            JpegXlTranscoder.RecompressJpeg(input, output);
            Assert.Equal(jpegXl, output.ToArray());
        }

        using (var input = CreateStream(jpeg, isAsynchronous: true))
        using (var output = new MemoryStream())
        {
            await JpegXlTranscoder.RecompressJpegAsync(input, output, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(jpegXl, output.ToArray());
        }

        using (var input = CreateStream(jpegXl, isAsynchronous: false))
        using (var output = new MemoryStream())
        {
            Assert.True(JpegXlTranscoder.HasJpegReconstructionData(new MemoryStream(jpegXl)));
            Assert.True(JpegXlTranscoder.TryReconstructJpeg(input, output));
            Assert.Equal(jpeg, output.ToArray());
        }

        using (var input = CreateStream(jpegXl, isAsynchronous: true))
        using (var output = new MemoryStream())
        {
            Assert.True(await JpegXlTranscoder.HasJpegReconstructionDataAsync(new MemoryStream(jpegXl), cancellationToken: TestContext.Current.CancellationToken));
            Assert.True(await JpegXlTranscoder.TryReconstructJpegAsync(input, output, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(jpeg, output.ToArray());
        }

        using (var input = new MemoryStream(jpegXl))
        using (var output = new MemoryStream())
        {
            await JpegXlTranscoder.ReconstructJpegAsync(input, output, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(jpeg, output.ToArray());
        }

        // Paths: the destination is created, then replaced
        var directory = Directory.CreateTempSubdirectory("mfi-jxl-transcoder-");
        try
        {
            var jpegPath = Path.Combine(directory.FullName, "source.jpg");
            var jpegXlPath = Path.Combine(directory.FullName, "recompressed.jxl");
            var rebuiltPath = Path.Combine(directory.FullName, "rebuilt.jpg");
            await File.WriteAllBytesAsync(jpegPath, jpeg, TestContext.Current.CancellationToken);
            JpegXlTranscoder.RecompressJpeg(jpegPath, jpegXlPath);
            Assert.Equal(jpegXl, await File.ReadAllBytesAsync(jpegXlPath, TestContext.Current.CancellationToken));
            await JpegXlTranscoder.RecompressJpegAsync(jpegPath, jpegXlPath, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(jpegXl, await File.ReadAllBytesAsync(jpegXlPath, TestContext.Current.CancellationToken));
            Assert.True(JpegXlTranscoder.HasJpegReconstructionData(jpegXlPath));
            Assert.True(JpegXlTranscoder.TryReconstructJpeg(jpegXlPath, rebuiltPath));
            Assert.Equal(jpeg, await File.ReadAllBytesAsync(rebuiltPath, TestContext.Current.CancellationToken));
            JpegXlTranscoder.ReconstructJpeg(jpegXlPath, rebuiltPath);
            await JpegXlTranscoder.ReconstructJpegAsync(jpegXlPath, rebuiltPath, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(jpeg, await File.ReadAllBytesAsync(rebuiltPath, TestContext.Current.CancellationToken));
            Assert.Equal(["rebuilt.jpg", "recompressed.jxl", "source.jpg"], directory.GetFiles().Select(file => file.Name).Order(StringComparer.Ordinal));

            // A file that cannot be rebuilt creates nothing
            var missingPath = Path.Combine(directory.FullName, "missing.jpg");
            Assert.Throws<UnsupportedImageFeatureException>(() => JpegXlTranscoder.ReconstructJpeg(jpegPath, missingPath));
            Assert.False(File.Exists(missingPath));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void DamagedReconstructionDataAreRefusedOrGiveAFile()
    {
        var jpeg = GoldenCorpus.Default.Get("jpeg/progressive-422-odd-restart").ReadInput();
        using var recompressed = new MemoryStream();
        JpegXlTranscoder.RecompressJpeg(jpeg, recompressed);
        var data = recompressed.ToArray();

        // The box that describes the JPEG file follows the signature and the file type boxes
        var start = 12 + 20 + 8;
        var length = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12 + 20)) - 8;
        Assert.Equal("jbrd"u8, data.AsSpan(12 + 20 + 4, 4));
        var failures = 0;
        for (var i = 0; i < length; i++)
        {
            var damaged = (byte[])data.Clone();
            damaged[start + i] ^= 0x10;
            using var output = new MemoryStream();
            try
            {
                // Another description may still be the one of a JPEG file: nothing in the box tells that it changed
                Assert.True(JpegXlTranscoder.TryReconstructJpeg(damaged, output));
            }
            catch (InvalidImageContentException)
            {
                Assert.Equal(0, output.Length);
                failures++;
            }
        }

        Assert.True(failures > length / 2, $"{failures} of {length} damaged files were refused.");
    }

    /// <summary>
    /// The data that rebuild the JPEG file may follow the codestream, which no encoder writes: the file is rebuilt all the
    /// same, from a stream that cannot seek as from memory, whether the codestream is in one box (the library) or in
    /// several (cjxl), and with the metadata boxes on either side.
    /// </summary>
    [Theory]
    [InlineData("jpeg/baseline-420", false)]
    [InlineData("jpeg/progressive-422-odd-restart", false)]
    [InlineData("jpeg/metadata-icc-exif-dpcm", false)]
    [InlineData("jpeg/metadata-density-xmp-comment", false)]
    [InlineData("jpeg/metadata-icc-exif-dpcm", true)]
    [InlineData("jxl/jpeg-420", false)]
    [InlineData("jxl/jpeg-gray", false)]
    public async Task ReconstructionDataThatFollowTheCodestreamRebuildTheFile(string id, bool everyBoxAfter)
    {
        byte[] jpeg;
        byte[] data;
        if (id.StartsWith("jxl/", StringComparison.Ordinal))
        {
            // A file of cjxl, whose JPEG file is the one it gives back as it is
            data = GoldenCorpus.Default.Get(id).ReadInput();
            using var source = new MemoryStream();
            JpegXlTranscoder.ReconstructJpeg(data, source);
            jpeg = source.ToArray();
        }
        else
        {
            jpeg = GoldenCorpus.Default.Get(id).ReadInput();
            using var recompressed = new MemoryStream();
            JpegXlTranscoder.RecompressJpeg(jpeg, recompressed);
            data = recompressed.ToArray();
        }

        var boxes = SplitBoxes(data);
        Assert.Contains(boxes, box => box.Type == "jbrd");
        var isCodestream = (string type) => type is "jxlc" or "jxlp";
        var moved = Join(
        [
            .. boxes.Where(box => box.Type is "JXL " or "ftyp" or "jxll"),
            .. boxes.Where(box => !everyBoxAfter && box.Type is not ("JXL " or "ftyp" or "jxll" or "jbrd") && !isCodestream(box.Type)),
            .. boxes.Where(box => isCodestream(box.Type)),
            .. boxes.Where(box => everyBoxAfter && box.Type is not ("JXL " or "ftyp" or "jxll" or "jbrd") && !isCodestream(box.Type)),
            .. boxes.Where(box => box.Type == "jbrd"),
        ]);
        Assert.HasCount(data.Length, moved);
        Assert.NotEqual(data, moved);

        Assert.True(JpegXlTranscoder.HasJpegReconstructionData(moved));
        using var output = new MemoryStream();
        JpegXlTranscoder.ReconstructJpeg(moved, output);
        Assert.Equal(jpeg, output.ToArray());

        using (var stream = CreateStream(moved, isAsynchronous: false))
        {
            Assert.True(JpegXlTranscoder.HasJpegReconstructionData(stream));
        }

        using var fromStream = new MemoryStream();
        using (var stream = CreateStream(moved, isAsynchronous: true))
        {
            Assert.True(await JpegXlTranscoder.TryReconstructJpegAsync(stream, fromStream, cancellationToken: TestContext.Current.CancellationToken));
        }

        Assert.Equal(jpeg, fromStream.ToArray());

        // The pixels do not depend on where the box is
        using var expected = Image.Load<Rgba32>(data);
        using var actual = Image.Load<Rgba32>(moved);
        Assert.Equal(0, GetMaxDifference(expected, actual));
    }

    /// <summary>
    /// A container whose frame could be the one of a JPEG file is read to its end before it is said to hold no
    /// reconstruction data; a frame that could not be is not looked past, whatever follows it; two boxes are an error
    /// wherever they are; and a box that is cut is an error, not an absence.
    /// </summary>
    [Fact]
    public void ReconstructionDataAreLookedForToTheEndOfAFileThatCouldHoldAJpegFile()
    {
        var jpeg = GoldenCorpus.Default.Get("jpeg/baseline-420").ReadInput();
        using var recompressed = new MemoryStream();
        JpegXlTranscoder.RecompressJpeg(jpeg, recompressed);
        var boxes = SplitBoxes(recompressed.ToArray());
        var description = boxes.Single(box => box.Type == "jbrd");
        var others = boxes.Where(box => box.Type != "jbrd").ToArray();

        // The frame of a JPEG file without the box: nothing to rebuild
        var without = Join(others);
        AssertNotRebuilt(without);

        // Two boxes: before and after the codestream, or both after it
        foreach (var twice in new[] { Join([.. boxes, description]), Join([.. others, description, description]) })
        {
            using var output = new MemoryStream();
            Assert.Throws<InvalidImageContentException>(() => JpegXlTranscoder.TryReconstructJpeg(twice, output));
            Assert.Equal(0, output.Length);
        }

        // The box is cut
        var cut = Join([.. others, description])[..^5];
        Assert.Throws<InvalidImageContentException>(() => JpegXlTranscoder.HasJpegReconstructionData(cut));
        using (var output = new MemoryStream())
        {
            Assert.Throws<InvalidImageContentException>(() => JpegXlTranscoder.TryReconstructJpeg(cut, output));
            Assert.Equal(0, output.Length);
        }

        // A frame that is not the one of a JPEG file (it is stored in XYB), in a container, followed by the box: the box is not looked for
        using var image = new Image<Rgb24>(40, 24);
        using var encoded = new MemoryStream();
        image.Save(encoded, new JpegXlEncoder { Compression = JpegXlCompression.Lossy });
        AssertNotRebuilt([.. JpegXlTestFiles.ContainerStart, .. JpegXlTestFiles.Box("jxlc", encoded.ToArray()), .. description.Bytes]);

        // A bare codestream has no box
        AssertNotRebuilt(encoded.ToArray());

        static void AssertNotRebuilt(byte[] data)
        {
            Assert.False(JpegXlTranscoder.HasJpegReconstructionData(data));
            using var output = new MemoryStream();
            Assert.False(JpegXlTranscoder.TryReconstructJpeg(data, output));
            Assert.Throws<UnsupportedImageFeatureException>(() => JpegXlTranscoder.ReconstructJpeg(data, output));
            Assert.Equal(0, output.Length);
            using var loaded = Image.Load(data);
            Assert.True(loaded.Width > 0);
        }
    }

    [Fact]
    public void LimitsApplyToBothDirections()
    {
        var jpeg = GoldenCorpus.Default.Get("jpeg/baseline-420").ReadInput();
        using var recompressed = new MemoryStream();
        JpegXlTranscoder.RecompressJpeg(jpeg, recompressed);
        var options = new JpegXlTranscodeOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = 4 } } };
        using var output = new MemoryStream();
        Assert.Throws<ImageResourceLimitException>(() => JpegXlTranscoder.RecompressJpeg(jpeg, output, options));
        Assert.Throws<ImageResourceLimitException>(() => JpegXlTranscoder.ReconstructJpeg(recompressed.ToArray(), output, options));
        Assert.Equal(0, output.Length);
    }

    private static void AssertRoundTrips(byte[] jpeg, string context)
    {
        using var recompressed = new MemoryStream();
        JpegXlTranscoder.RecompressJpeg(jpeg, recompressed);
        using var rebuilt = new MemoryStream();
        JpegXlTranscoder.ReconstructJpeg(recompressed.ToArray(), rebuilt);
        Assert.True(jpeg.AsSpan().SequenceEqual(rebuilt.ToArray()), $"{context}: the rebuilt file differs.");
    }

    /// <summary>Splits a JPEG XL container into its boxes; a last box that extends to the end of the file is given its size.</summary>
    private static List<(string Type, byte[] Bytes)> SplitBoxes(byte[] data)
    {
        var boxes = new List<(string Type, byte[] Bytes)>();
        var position = 0;
        while (position < data.Length)
        {
            var size = (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position));
            var type = System.Text.Encoding.ASCII.GetString(data, position + 4, 4);
            Assert.NotEqual(1, size);
            if (size == 0)
            {
                size = data.Length - position;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(position), (uint)size);
            }

            boxes.Add((type, data.AsSpan(position, (int)size).ToArray()));
            position += (int)size;
        }

        return boxes;
    }

    private static byte[] Join(IEnumerable<(string Type, byte[] Bytes)> boxes) => [.. boxes.SelectMany(box => box.Bytes)];

    private static TestInputStream CreateStream(byte[] data, bool isAsynchronous)
        => new(data) { Seekable = false, MaxBytesPerRead = 97, ForbidSynchronousReads = isAsynchronous };

    private static int GetMaxDifference(Image<Rgba32> expected, Image<Rgba32> actual)
    {
        var a = new Rgba32[expected.Width * expected.Height];
        var b = new Rgba32[a.Length];
        expected.Frames[0].CopyPixelDataTo(a);
        actual.Frames[0].CopyPixelDataTo(b);
        var max = 0;
        for (var i = 0; i < a.Length; i++)
        {
            max = Math.Max(max, Math.Max(Math.Abs(a[i].R - b[i].R), Math.Max(Math.Abs(a[i].G - b[i].G), Math.Abs(a[i].B - b[i].B))));
        }

        return max;
    }
}
