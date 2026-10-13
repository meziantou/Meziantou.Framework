using System.Security.Cryptography;
using System.Text.Json;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Validates the committed corpus before any pixel comparison runs. Golden decode tests must only
/// consume fixtures that pass these checks.
/// </summary>
public sealed class CorpusManifestTests
{
    [Fact]
    public void CommittedManifestIsValid()
    {
        var root = FixtureRoot.GetDirectory();
        var manifest = FixtureManifestLoader.Load(root / FixtureRoot.ManifestFileName);
        var errors = FixtureManifestValidator.Validate(manifest, root);
        Assert.Empty(errors);
    }

    [Fact]
    public void EveryFileIsKeptByTheGeneratorsOfTheOtherSections()
    {
        // A generator replaces its own entries and files, and copies the files the other sections reference from the
        // lists of the corpus generator: its write pass removes every committed file that is in none of them
        var root = FixtureRoot.GetDirectory();
        using var document = JsonDocument.Parse(File.ReadAllText(root / FixtureRoot.ManifestFileName));
        var manifest = (Obj)ToGeneratorValue(document.RootElement)!;
        var kept = CorpusDriver.ReferencedFiles(manifest, root);
        var files = CorpusDriver.CorpusFiles(root);
        files.Remove(FixtureRoot.ManifestFileName);
        Assert.NotEmpty(files);

        var removed = files.Except(kept, StringComparer.Ordinal).ToList();
        Assert.Empty(removed, $"The lists of CorpusDriver.Files.cs do not name {removed.Count} files of the corpus, which the write pass of a generator would delete:\n  " + string.Join("\n  ", removed.Take(20)));
    }

    [Fact]
    public void SchemaVersionMatchesHarness()
    {
        using var schema = LoadSchema();
        var version = schema.RootElement.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetInt32();
        Assert.Equal(FixtureManifest.CurrentSchemaVersion, version);
    }

    [Fact]
    public void SchemaEnumerationsMatchHarness()
    {
        using var schema = LoadSchema();
        var defs = schema.RootElement.GetProperty("$defs");
        AssertSameSet(FixtureManifestValidator.AllowedLicenses, Enum(defs, "provenance", "license"));
        AssertSameSet(FixtureManifestValidator.AllowedOrigins, Enum(defs, "provenance", "origin"));
        AssertSameSet(FixtureKinds.All, Enum(defs, "fixture", "kind"));
        AssertSameSet(FixtureManifestValidator.AllowedFormats, Enum(defs, "fixture", "format"));
        AssertSameSet(RawPixelLayout.All.Select(layout => layout.Name), Enum(defs, "buffer", "layout"));
        AssertSameSet(RawBufferReader.AllowedCompressions, Enum(defs, "buffer", "compression"));
        AssertSameSet(FixtureManifestValidator.AllowedPixelFormats, Enum(defs, "expected", "pixelFormat"));
        AssertSameSet(FixtureManifestValidator.AllowedColorModels, Enum(defs, "expected", "colorModel"));
        AssertSameSet(FixtureManifestValidator.AllowedIccProfiles, Enum(defs, "expected", "iccProfile"));
        AssertSameSet(FixtureManifestValidator.AllowedCrossCheckResults, Enum(defs, "crossCheck", "result"));
        AssertSameSet(FontEntry.AllowedFlavors, Enum(defs, "font", "flavor"));
        AssertSameSet(FontEntry.AllowedModifications, Enum(defs, "font", "modification"));
        AssertSameSet(FixtureManifestValidator.AllowedAv1Subsamplings, Enum(defs, "av1Vector", "subsampling"));
        AssertSameSet(FixtureManifestValidator.AllowedAv1BitDepths.Select(depth => depth.ToString(CultureInfo.InvariantCulture)), defs.GetProperty("av1Vector").GetProperty("properties").GetProperty("bitDepth").GetProperty("enum").EnumerateArray().Select(item => item.GetInt32().ToString(CultureInfo.InvariantCulture)));
        Assert.StartsWith("^" + FixtureManifestValidator.Av1VectorPrefix, defs.GetProperty("av1Vector").GetProperty("properties").GetProperty("id").GetProperty("pattern").GetString());
        AssertSameSet(FixtureManifestValidator.AllowedLimits, defs.GetProperty("decodeOptions").GetProperty("properties").GetProperty("limits").GetProperty("properties").EnumerateObject().Select(property => property.Name));

        var comparison = defs.GetProperty("comparison").GetProperty("properties");
        Assert.Equal(ComparisonPolicy.AbsoluteErrorCeiling, comparison.GetProperty("maxAbsoluteError").GetProperty("maximum").GetInt32());
        Assert.Equal(ComparisonPolicy.MeanAbsoluteErrorCeiling, comparison.GetProperty("maxMeanAbsoluteError").GetProperty("maximum").GetDouble());
    }

    [Fact]
    public void LibraryEnumerationNamesAreKnownToTheHarness()
    {
        // The manifest uses library member names; keep both lists in sync when the library adds members
        AssertSameSet(System.Enum.GetNames<PixelFormat>().Where(name => name != nameof(PixelFormat.Unknown)), FixtureManifestValidator.AllowedPixelFormats);
        AssertSameSet(System.Enum.GetNames<ImageColorModel>().Where(name => name != nameof(ImageColorModel.Unknown)), FixtureManifestValidator.AllowedColorModels);
        AssertSameSet(typeof(ImageResourceLimits).GetProperties().Where(property => property.Name.StartsWith("Max", StringComparison.Ordinal)).Select(property => property.Name), FixtureManifestValidator.AllowedLimits);
        foreach (var fixture in LoadManifest().Fixtures.Where(fixture => fixture.ExpectedError is not null))
        {
            if (fixture.ExpectedError!.Format is { } format)
            {
                Assert.True(System.Enum.TryParse<ImageFormat>(format, ignoreCase: false, out _), $"{fixture.Id}: unknown ImageFormat '{format}'.");
            }

            if (fixture.ExpectedError.LimitKind is { } kind)
            {
                Assert.True(System.Enum.TryParse<ImageResourceLimitKind>(kind, ignoreCase: false, out _), $"{fixture.Id}: unknown ImageResourceLimitKind '{kind}'.");
            }

            Assert.NotNull(typeof(ImageException).Assembly.GetTypes().SingleOrDefault(type => type.Name == fixture.ExpectedError.Exception && typeof(Exception).IsAssignableFrom(type)));
        }
    }

    [Fact]
    public void ManifestRecordsGeneratorAndPinnedTools()
    {
        var manifest = LoadManifest();
        Assert.NotNull(manifest.Generator);
        Assert.Equal("tools/Meziantou.Framework.Imaging.CorpusGenerator/GoldenCorpus.cs", manifest.Generator!.Script);
        Assert.Contains(manifest.Generator.Tools, tool => tool.StartsWith("ffmpeg ", StringComparison.Ordinal));
        Assert.Contains(manifest.Generator.Tools, tool => tool.StartsWith("libjpeg-turbo ", StringComparison.Ordinal));
        foreach (var fixture in manifest.Fixtures)
        {
            Assert.True(fixture.Provenance.Tools is { Count: > 0 }, $"{fixture.Id}: tools must be recorded.");
            // WebP, QOI, BMP, TGA and Netpbm come from their own generators (libwebp tools, the qoi.h reference, FFmpeg; any
            // OS); the golden generator keeps them unchanged
            var generator = fixture.Format switch
            {
                "webp" => "tools/Meziantou.Framework.Imaging.CorpusGenerator/WebPCorpus.cs",
                "qoi" => "tools/Meziantou.Framework.Imaging.CorpusGenerator/QoiCorpus.cs",
                "bmp" => "tools/Meziantou.Framework.Imaging.CorpusGenerator/BmpCorpus.cs",
                "tga" => "tools/Meziantou.Framework.Imaging.CorpusGenerator/TgaCorpus.cs",
                "pnm" => "tools/Meziantou.Framework.Imaging.CorpusGenerator/PnmCorpus.cs",
                "avif" => "tools/Meziantou.Framework.Imaging.CorpusGenerator/AvifCorpus.Container.cs",
                "jxl" => "tools/Meziantou.Framework.Imaging.CorpusGenerator/JxlCorpus.cs",
                "jp2" => "tools/Meziantou.Framework.Imaging.CorpusGenerator/Jp2Corpus.cs",
                _ => "tools/Meziantou.Framework.Imaging.CorpusGenerator/GoldenCorpus.cs",
            };
            Assert.StartsWith(generator, fixture.Provenance.Generator);
            if (fixture.Reference?.Method == FixtureReference.Decoded)
            {
                Assert.True(fixture.Reference.CrossChecks is { Count: > 0 }, $"{fixture.Id}: decoded references must be cross-checked with another independent decoder.");
            }

            if (fixture.Reference is { Method: FixtureReference.HandComputed } reference)
            {
                Assert.True(reference.CrossChecks is { Count: > 0 }, $"{fixture.Id}: hand-computed references are cross-checked with an independent decoder.");
            }
        }
    }

    [Theory]
    [InlineData("png.interlace=adam7")]
    [InlineData("png.bitDepth=1")]
    [InlineData("png.bitDepth=2")]
    [InlineData("png.bitDepth=4")]
    [InlineData("png.bitDepth=16")]
    [InlineData("png.colorType=3")]
    [InlineData("png.chunk=tRNS")]
    [InlineData("apng.blend=over,source")]
    [InlineData("apng.dispose=background,none,previous")]
    [InlineData("apng.poster=separate")]
    [InlineData("gif.disposal=0,1,2,3")]
    [InlineData("gif.version=87a")]
    [InlineData("gif.interlaced")]
    [InlineData("gif.localPalette")]
    [InlineData("gif.lzw.tableFull")]
    [InlineData("gif.lzw.deferredClear")]
    [InlineData("gif.minCodeSize=2")]
    [InlineData("jpeg.process=baseline")]
    [InlineData("jpeg.process=progressive")]
    [InlineData("jpeg.sampling=4:2:0")]
    [InlineData("jpeg.sampling=4:2:2")]
    [InlineData("jpeg.sampling=4:4:4")]
    [InlineData("jpeg.components=1")]
    [InlineData("jpeg.app1=Exif")]
    [InlineData("jpeg.process=extended")]
    [InlineData("jpeg.sampling=4:4:0")]
    [InlineData("jpeg.sampling=4:1:1")]
    [InlineData("jpeg.quantization=16-bit")]
    [InlineData("jpeg.scanComponents=1,1,1")]
    [InlineData("jpeg.app14=Adobe")]
    [InlineData("jpeg.app2=ICC_PROFILE")]
    [InlineData("webp.layout=simple")]
    [InlineData("webp.layout=extended")]
    [InlineData("webp.bitstream=vp8")]
    [InlineData("webp.bitstream=vp8l")]
    [InlineData("webp.alpha=alph")]
    [InlineData("webp.alpha=vp8l")]
    [InlineData("webp.alph.compression=none")]
    [InlineData("webp.alph.compression=vp8l")]
    [InlineData("webp.vp8.filter=simple")]
    [InlineData("webp.vp8.filter=normal")]
    [InlineData("webp.vp8.segmentation")]
    [InlineData("webp.vp8l.transform=predictor")]
    [InlineData("webp.vp8l.transform=color-indexing")]
    [InlineData("webp.chunk=ICCP")]
    [InlineData("webp.chunk=EXIF")]
    [InlineData("webp.chunk=XMP")]
    [InlineData("webp.animation")]
    [InlineData("webp.blend=alpha")]
    [InlineData("webp.blend=none")]
    [InlineData("webp.dispose=background")]
    [InlineData("webp.frame=partial")]
    [InlineData("webp.frames=mixed")]
    [InlineData("webp.loop=infinite")]
    [InlineData("webp.loop=finite")]
    [InlineData("qoi.channels=3")]
    [InlineData("qoi.channels=4")]
    [InlineData("qoi.colorspace=linear")]
    [InlineData("qoi.op=index")]
    [InlineData("qoi.op=diff")]
    [InlineData("qoi.op=luma")]
    [InlineData("qoi.op=rgb")]
    [InlineData("qoi.op=rgba")]
    [InlineData("qoi.op=run")]
    [InlineData("qoi.run=62")]
    [InlineData("qoi.run.crossesRow")]
    [InlineData("bmp.bpp=1")]
    [InlineData("bmp.bpp=4")]
    [InlineData("bmp.bpp=8")]
    [InlineData("bmp.bpp=16")]
    [InlineData("bmp.bpp=24")]
    [InlineData("bmp.bpp=32")]
    [InlineData("bmp.rowOrder=bottom-up")]
    [InlineData("bmp.rowOrder=top-down")]
    [InlineData("bmp.rowPadding")]
    [InlineData("bmp.compression=bitfields")]
    [InlineData("bmp.compression=rle8")]
    [InlineData("bmp.compression=rle4")]
    [InlineData("bmp.compression=rle24")]
    [InlineData("bmp.rle=absolute")]
    [InlineData("bmp.rle=absolutePadding")]
    [InlineData("bmp.rle=delta")]
    [InlineData("bmp.rle=earlyEndOfLine")]
    [InlineData("bmp.rle=earlyEndOfBitmap")]
    [InlineData("bmp.alphaMask=yes")]
    [InlineData("bmp.alphaMask=no")]
    [InlineData("bmp.gapBeforePixels")]
    [InlineData("bmp.resolution")]
    [InlineData("bmp.header=12")]
    [InlineData("bmp.header=16")]
    [InlineData("bmp.header=40")]
    [InlineData("bmp.header=56")]
    [InlineData("bmp.header=64")]
    [InlineData("bmp.header=108")]
    [InlineData("bmp.paletteEntry=rgbtriple")]
    [InlineData("bmp.paletteFromOffset")]
    [InlineData("tga.imageType=1")]
    [InlineData("tga.imageType=2")]
    [InlineData("tga.imageType=3")]
    [InlineData("tga.imageType=10")]
    [InlineData("tga.imageType=11")]
    [InlineData("tga.depth=15")]
    [InlineData("tga.depth=16")]
    [InlineData("tga.depth=24")]
    [InlineData("tga.depth=32")]
    [InlineData("tga.rowOrder=top-down")]
    [InlineData("tga.rowOrder=bottom-up")]
    [InlineData("tga.columnOrder=right-to-left")]
    [InlineData("tga.alphaBits=0")]
    [InlineData("tga.alphaBits=1")]
    [InlineData("tga.alphaBits=8")]
    [InlineData("tga.colorMap.offset")]
    [InlineData("tga.rle.run")]
    [InlineData("tga.rle.raw")]
    [InlineData("tga.rle.packet=128")]
    [InlineData("tga.rle.crossesRow")]
    [InlineData("tga.idField")]
    [InlineData("tga.footer")]
    [InlineData("pnm.magic=P1")]
    [InlineData("pnm.magic=P2")]
    [InlineData("pnm.magic=P3")]
    [InlineData("pnm.magic=P4")]
    [InlineData("pnm.magic=P5")]
    [InlineData("pnm.magic=P6")]
    [InlineData("pnm.magic=P7")]
    [InlineData("pnm.tuple=BLACKANDWHITE")]
    [InlineData("pnm.tuple=GRAYSCALE_ALPHA")]
    [InlineData("pnm.tuple=RGB_ALPHA")]
    [InlineData("pnm.maxval=1")]
    [InlineData("pnm.maxval=255")]
    [InlineData("pnm.maxval=65535")]
    [InlineData("pnm.maxval.normalized")]
    [InlineData("pnm.comments")]
    [InlineData("pnm.pbm.rowPadding")]
    [InlineData("avif.sequence")]
    [InlineData("avif.alpha")]
    [InlineData("avif.grid=2x2")]
    [InlineData("avif.clap")]
    [InlineData("avif.filmGrain")]
    [InlineData("avif.bitDepth=10")]
    [InlineData("avif.bitDepth=12")]
    [InlineData("avif.subsampling=4:2:2")]
    [InlineData("avif.layout=media-first")]
    [InlineData("avif.seq.editList=none")]
    [InlineData("avif.layers=2")]
    [InlineData("avif.layers=3")]
    [InlineData("avif.layers=4")]
    [InlineData("avif.layers.scaled")]
    [InlineData("avif.a1lx")]
    [InlineData("avif.lsel=0")]
    [InlineData("avif.lsel=65535")]
    [InlineData("avif.a1op=1")]
    public void StarterCorpusCoversFeature(string feature)
    {
        Assert.Contains(LoadManifest().Fixtures, fixture => fixture.Kind == FixtureKinds.Valid && fixture.HasFeature(feature));
    }

    [Theory]
    [InlineData(FixtureKinds.Invalid, "png")]
    [InlineData(FixtureKinds.Invalid, "gif")]
    [InlineData(FixtureKinds.Unsupported, "gif")]
    [InlineData(FixtureKinds.Unsupported, "jpeg")]
    [InlineData(FixtureKinds.Invalid, "jpeg")]
    [InlineData(FixtureKinds.Limit, "jpeg")]
    [InlineData(FixtureKinds.Limit, "png")]
    [InlineData(FixtureKinds.Limit, "gif")]
    [InlineData(FixtureKinds.Invalid, "webp")]
    [InlineData(FixtureKinds.Unsupported, "webp")]
    [InlineData(FixtureKinds.Limit, "webp")]
    [InlineData(FixtureKinds.Invalid, "qoi")]
    [InlineData(FixtureKinds.Limit, "qoi")]
    [InlineData(FixtureKinds.Invalid, "bmp")]
    [InlineData(FixtureKinds.Unsupported, "bmp")]
    [InlineData(FixtureKinds.Limit, "bmp")]
    [InlineData(FixtureKinds.Invalid, "tga")]
    [InlineData(FixtureKinds.Unsupported, "tga")]
    [InlineData(FixtureKinds.Limit, "tga")]
    [InlineData(FixtureKinds.Invalid, "pnm")]
    [InlineData(FixtureKinds.Unsupported, "pnm")]
    [InlineData(FixtureKinds.Limit, "pnm")]
    [InlineData(FixtureKinds.Invalid, "avif")]
    [InlineData(FixtureKinds.Unsupported, "avif")]
    [InlineData(FixtureKinds.Limit, "avif")]
    [InlineData(FixtureKinds.Invalid, "jxl")]
    [InlineData(FixtureKinds.Unsupported, "jxl")]
    [InlineData(FixtureKinds.Limit, "jxl")]
    [InlineData(FixtureKinds.Invalid, "jp2")]
    [InlineData(FixtureKinds.Unsupported, "jp2")]
    [InlineData(FixtureKinds.Limit, "jp2")]
    public void StarterCorpusHasErrorFixtures(string kind, string format)
    {
        Assert.Contains(LoadManifest().Fixtures, fixture => fixture.Kind == kind && fixture.Format == format && fixture.ExpectedError is not null && fixture.Expected is null);
    }

    [Fact]
    public void Av1VectorsRecordGeneratorDecodersAndPinnedTools()
    {
        const string Script = "tools/Meziantou.Framework.Imaging.CorpusGenerator/AvifCorpus.cs";
        var vectors = LoadManifest().Av1Vectors;
        Assert.NotNull(vectors);
        Assert.Equal(Script, vectors.Generator.Script);
        foreach (var tool in new[] { "ffmpeg ", "libaom ", "SVT-AV1 ", "libdav1d ", "libavif " })
        {
            Assert.Contains(vectors.Generator.Tools, name => name.StartsWith(tool, StringComparison.Ordinal));
        }

        // Two independent decoders produced every digest, and a third implementation parsed the headers
        Assert.Contains(vectors.Decoders, decoder => decoder.Name.StartsWith("libdav1d ", StringComparison.Ordinal) && decoder.Command.Contains("-c:v libdav1d ", StringComparison.Ordinal));
        Assert.Contains(vectors.Decoders, decoder => decoder.Name.StartsWith("libaom ", StringComparison.Ordinal) && decoder.Command.Contains("-c:v libaom-av1 ", StringComparison.Ordinal));
        Assert.Contains("trace_headers", vectors.HeaderParser.Command, StringComparison.Ordinal);

        Assert.NotEmpty(vectors.Entries);
        Assert.All(vectors.Entries, vector =>
        {
            Assert.Equal("generated", vector.Provenance.Origin);
            Assert.StartsWith(Script, vector.Provenance.Generator);
            Assert.True(vector.Provenance.Tools is { Count: > 0 }, $"{vector.Id}: tools must be recorded.");

            // The recorded format is the one the digests are computed in
            var expectedFormat = (vector.Subsampling, vector.HasFeature("av1.matrix=identity")) switch
            {
                ("4:0:0", _) => "gray",
                (_, true) => "gbrp",
                var (subsampling, _) => "yuv" + subsampling.Replace(":", "", StringComparison.Ordinal) + "p",
            };
            Assert.Equal(expectedFormat + (vector.BitDepth == 8 ? "" : vector.BitDepth + "le"), vector.PixelFormat);
            Assert.True(vector.HasFeature($"av1.bitDepth={vector.BitDepth}") && vector.HasFeature($"av1.subsampling={vector.Subsampling}"), $"{vector.Id}: the features must state the bit depth and the subsampling.");

            if (vector.HasFeatureKey("av1.spatial_layers"))
            {
                // A layered stream: no encoder of the FFmpeg build codes spatial layers, so it is the payload of the coded
                // item of files that avifenc writes, single-threaded, one file and one command per temporal unit
                Assert.Contains(vector.Provenance.Tools!, tool => tool.StartsWith("libavif ", StringComparison.Ordinal) && tool.Contains("avifenc", StringComparison.Ordinal));
                var files = vector.Provenance.Parameters!.Value.GetProperty("files");
                Assert.Equal(vector.FrameCount, vector.Provenance.Commands!.Count);
                Assert.Equal(vector.FrameCount, files.GetArrayLength());
                Assert.All(vector.Provenance.Commands, layered =>
                {
                    Assert.StartsWith("avifenc -j 1 ", layered);
                    Assert.True(layered.Contains(" --layered ", StringComparison.Ordinal) || layered.Contains(" --progressive ", StringComparison.Ordinal), $"{vector.Id}: {layered}");
                    Assert.EndsWith(" {output}.avif", layered, StringComparison.Ordinal);
                });
                Assert.All(files.EnumerateArray(), file => Assert.Equal(GetFeatureValue(vector, "av1.spatial_layers="), file.GetProperty("spatialLayers").GetInt32(), message: $"{vector.Id}: every file has the layers of the operating point."));
                return;
            }

            // One deterministic, single-threaded, bit-exact encoder command that writes a raw OBU stream
            var command = Assert.Single(vector.Provenance.Commands!);
            Assert.StartsWith("ffmpeg ", command);
            Assert.Contains(" -f lavfi ", command, StringComparison.Ordinal);
            Assert.Contains(" -threads 1 ", command, StringComparison.Ordinal);
            Assert.Contains(" -flags +bitexact ", command, StringComparison.Ordinal);
            Assert.EndsWith(" -f obu {input}", command, StringComparison.Ordinal);
            // SVT-AV1 writes an empty stream for inter frames with lp=1, and not always the same stream without lp: its
            // key-frame vectors pin lp=1 and its inter vectors lp=2, the lowest level of parallelism that writes a stream
            var singleThreadedEncoder = command.Contains(" -c:v libaom-av1 ", StringComparison.Ordinal)
                ? command.Contains(" -row-mt 0 ", StringComparison.Ordinal)
                : command.Contains(" -c:v libsvtav1 ", StringComparison.Ordinal) && command.Contains(vector.HasFeature("av1.frame_types=inter") ? " -svtav1-params lp=2" : " -svtav1-params lp=1", StringComparison.Ordinal);
            Assert.True(singleThreadedEncoder, $"{vector.Id}: {command}");

            // Only the vectors an encoder did not write alone have parameters: the patch or the hand-authored frames
            Assert.Equal(vector.HasFeature("av1.patched_header") || vector.HasFeature("av1.hand_authored"), vector.Provenance.Parameters is not null);
        });
    }

    [Fact]
    public void Av1LayeredVectorsSignalTheirOperatingPointsAndTheirLayers()
    {
        // What the tags of a layered vector say together, as avifenc writes its streams: one operating point per number of
        // layers, the first one with every layer; the layers the stream holds; the sizes of the layers when they differ
        var vectors = LoadManifest().Av1Vectors!.Entries;
        var layered = vectors.Where(vector => vector.HasFeatureKey("av1.spatial_layers")).ToList();
        Assert.NotEmpty(layered);
        Assert.All(layered, vector =>
        {
            var layers = GetFeatureValue(vector, "av1.spatial_layers=")!.Value;
            Assert.True(layers is >= 2 and <= 4, $"{vector.Id}: AV1 has four spatial layers.");
            Assert.Equal(layers, GetFeatureValue(vector, "av1.operating_points="));
            Assert.True(vector.HasFeature(string.Create(CultureInfo.InvariantCulture, $"av1.operating_point_idc=0x{((1 << layers) - 1) << 8 | 1:X3}")), $"{vector.Id}: the first operating point holds every layer.");
            var present = GetFeatureValue(vector, "av1.spatial_layers.present=");
            Assert.True(present is null || (present >= 1 && present < layers), $"{vector.Id}: the tag of the layers that are present is only recorded when some are missing.");
            Assert.Equal(present is not null, vector.Provenance.Parameters!.Value.TryGetProperty("truncationOf", out _));

            // A stream that holds several layers has inter frames: every layer after the first one predicts from the one below
            Assert.Equal((present ?? layers) > 1, vector.HasFeature("av1.frame_types=inter"));
            var sizes = vector.Features.SingleOrDefault(feature => feature.StartsWith("av1.spatial_layer_sizes=", StringComparison.Ordinal))?.Split('=')[1].Split(',');
            if (sizes is not null)
            {
                Assert.HasCount(present ?? layers, sizes);
                Assert.HasCountGreaterThan(1, sizes.Distinct(StringComparer.Ordinal));

                // The last layer the stream holds is the output frame
                Assert.Equal(sizes[^1], string.Create(CultureInfo.InvariantCulture, $"{vector.Frames[0].Width}x{vector.Frames[0].Height}"));
            }
        });

        // No other vector has scalability information
        Assert.All(vectors.Except(layered), vector => Assert.DoesNotContain(vector.Features, feature => feature.StartsWith("av1.operating_point", StringComparison.Ordinal) || feature.StartsWith("av1.spatial_layer", StringComparison.Ordinal), $"{vector.Id} has a scalability tag."));
    }

    [Fact]
    public void Av1TruncatedLayeredVectorsAreTheirSourceWithoutTheUnitsOfTheHigherLayers()
    {
        // The provenance of a truncated vector names the vector that holds every layer and the number of layers kept: its
        // stream must be the stream of that vector without the units whose extension names a higher spatial layer
        // (AV1 specification, section 5.3: the header byte, the extension byte when bit 2 is set, a LEB128 size), and
        // nothing else may differ. Both come from the same avifenc file
        var vectors = LoadManifest().Av1Vectors!.Entries;
        var truncated = vectors.Where(vector => vector.Provenance.Parameters is { } parameters && parameters.TryGetProperty("truncationOf", out _)).ToList();
        Assert.NotEmpty(truncated);
        foreach (var vector in truncated)
        {
            var parameters = vector.Provenance.Parameters!.Value;
            var source = vectors.Single(candidate => candidate.Id == parameters.GetProperty("truncationOf").GetString());
            var kept = parameters.GetProperty("keptSpatialLayers").GetInt32();
            Assert.False(source.Provenance.Parameters!.Value.TryGetProperty("truncationOf", out _));
            Assert.Equal(source.Provenance.Commands, vector.Provenance.Commands);
            Assert.Equal(source.Provenance.Parameters.Value.GetProperty("files").GetRawText(), parameters.GetProperty("files").GetRawText());
            Assert.Equal(kept, GetFeatureValue(vector, "av1.spatial_layers.present="));
            Assert.True(kept < GetFeatureValue(source, "av1.spatial_layers="));
            Assert.Equal((source.FrameCount, source.PixelFormat), (vector.FrameCount, vector.PixelFormat));

            var stream = File.ReadAllBytes(FixtureRoot.GetDirectory() / source.Input.Path);
            var expected = new List<byte>();
            var dropped = 0;
            for (var offset = 0; offset < stream.Length;)
            {
                var header = stream[offset];
                var hasExtension = (header & 0x04) != 0;
                var position = offset + (hasExtension ? 2 : 1);
                var size = 0;
                for (var shift = 0; ; shift += 7)
                {
                    var value = stream[position++];
                    size |= (value & 0x7F) << shift;
                    if ((value & 0x80) == 0)
                        break;
                }

                var spatialLayer = hasExtension ? (stream[offset + 1] >> 3) & 3 : 0;
                if (spatialLayer < kept)
                    expected.AddRange(stream.AsSpan(offset, position + size - offset));
                else
                    dropped++;

                offset = position + size;
            }

            Assert.True(dropped > 0);
            Assert.Equal(Convert.ToHexString(expected.ToArray()), Convert.ToHexString(File.ReadAllBytes(FixtureRoot.GetDirectory() / vector.Input.Path)));
        }
    }

    private static int? GetFeatureValue(Av1VectorEntry vector, string prefix)
    {
        var feature = vector.Features.SingleOrDefault(candidate => candidate.StartsWith(prefix, StringComparison.Ordinal));
        return feature is null ? null : int.Parse(feature[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Av1PatchedVectorsAreTheirSourceWithTwoBitsOfEveryPatchedHeaderChanged()
    {
        // A patched vector is the stream of another vector with the 2-bit interpolation_filter element of some frame
        // headers overwritten: same length, and no other bit differs
        var vectors = LoadManifest().Av1Vectors!.Entries;
        var patched = vectors.Where(vector => vector.HasFeature("av1.patched_header")).ToList();
        Assert.NotEmpty(patched);
        Assert.Contains(patched, vector => vector.HasFeature("av1.interpolation_filter=bilinear"));
        Assert.Contains(patched, vector => vector.HasFeature("av1.interpolation_filter=smooth"));
        foreach (var vector in patched)
        {
            var parameters = vector.Provenance.Parameters!.Value;
            var source = vectors.Single(candidate => candidate.Id == parameters.GetProperty("patchOf").GetString());
            Assert.False(source.HasFeature("av1.patched_header"));
            Assert.Equal(source.Input.Sha256, parameters.GetProperty("sourceSha256").GetString());
            Assert.Equal(source.Provenance.Commands, vector.Provenance.Commands);
            Assert.Contains("interpolation_filter", parameters.GetProperty("patch").GetString(), StringComparison.Ordinal);
            var offsets = parameters.GetProperty("patchedBitOffsets").EnumerateArray().Select(offset => offset.GetInt64()).ToList();
            Assert.NotEmpty(offsets);

            var original = File.ReadAllBytes(FixtureRoot.GetDirectory() / source.Input.Path);
            var changed = File.ReadAllBytes(FixtureRoot.GetDirectory() / vector.Input.Path);
            Assert.HasCount(original.Length, changed);
            var differences = new List<long>();
            for (var i = 0; i < original.Length; i++)
            {
                for (var bit = 0; bit < 8; bit++)
                {
                    if (((original[i] ^ changed[i]) & (0x80 >> bit)) != 0)
                        differences.Add((8L * i) + bit);
                }
            }

            Assert.NotEmpty(differences);
            Assert.All(differences, difference => Assert.Contains(offsets, offset => difference == offset || difference == offset + 1));
            Assert.All(offsets, offset => Assert.Contains(differences, difference => difference == offset || difference == offset + 1));

            // The source codes the regular filter in every frame header that has no switchable filter
            Assert.True(source.HasFeature("av1.interpolation_filter=eighttap") && !source.HasFeature("av1.interpolation_filter=bilinear") && !source.HasFeature("av1.interpolation_filter=smooth"));
            Assert.Equal((source.FrameCount, source.PixelFormat), (vector.FrameCount, vector.PixelFormat));
            Assert.NotEqual(source.Frames.Select(frame => string.Join(' ', frame.Planes)), vector.Frames.Select(frame => string.Join(' ', frame.Planes)));
        }
    }

    [Fact]
    public void Av1HandAuthoredVectorsStartWithTheKeyFrameOfTheirEncoderCommand()
    {
        // A hand-authored vector is one libaom key frame followed by frames the generator wrote: its provenance names the
        // recipe of the frames and the stream they were appended to, whose bytes the vector still starts with, except the
        // enable_dual_filter bit that the dual-filters recipe sets in the sequence header
        var vectors = LoadManifest().Av1Vectors!.Entries.Where(vector => vector.HasFeature("av1.hand_authored")).ToList();
        Assert.NotEmpty(vectors);
        Assert.Equal(["dual-filters", "segment-features"], vectors.Select(vector => vector.Provenance.Parameters!.Value.GetProperty("handAuthoredRecipe").GetString()!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        foreach (var vector in vectors)
        {
            var parameters = vector.Provenance.Parameters!.Value;
            var recipe = parameters.GetProperty("handAuthoredRecipe").GetString()!;
            Assert.StartsWith($"av1/hand-{recipe}-", vector.Id);
            Assert.False(string.IsNullOrWhiteSpace(parameters.GetProperty("description").GetString()));
            Assert.Contains(" -frames:v 1 ", Assert.Single(vector.Provenance.Commands!), StringComparison.Ordinal);
            Assert.True(vector.FrameCount > 1 && vector.HasFeature("av1.frame_types=inter") && vector.HasFeature("av1.disable_cdf_update"));

            var data = File.ReadAllBytes(FixtureRoot.GetDirectory() / vector.Input.Path);
            var keyFrameLength = parameters.GetProperty("keyFrameLength").GetInt32();
            Assert.HasCountGreaterThan(keyFrameLength, data);
            var keyFrame = data.AsSpan(0, keyFrameLength).ToArray();
            var expected = parameters.GetProperty("keyFrameSha256").GetString();
            if (recipe == "dual-filters")
            {
                // One bit of the sequence header was set: clearing the right one gives the stream the encoder wrote
                Assert.True(vector.HasFeature("av1.enable_dual_filter"));
                var found = false;
                for (var bit = 0; bit < 8 * Math.Min(keyFrameLength, 64) && !found; bit++)
                {
                    var mask = (byte)(0x80 >> (bit & 7));
                    if ((keyFrame[bit >> 3] & mask) == 0)
                        continue;

                    keyFrame[bit >> 3] ^= mask;
                    found = Convert.ToHexStringLower(SHA256.HashData(keyFrame)) == expected;
                    keyFrame[bit >> 3] ^= mask;
                }

                Assert.True(found, $"{vector.Id}: no single bit of the first 64 bytes turns the start of the stream into the recorded key frame stream.");
            }
            else
            {
                Assert.Equal(expected, Convert.ToHexStringLower(SHA256.HashData(keyFrame)));
            }
        }
    }

    [Fact]
    public void Av1VectorFrameCountsMatchTheirFeatures()
    {
        var vectors = LoadManifest().Av1Vectors!.Entries;
        Assert.All(vectors, vector => Assert.Equal(vector.FrameCount > 1, vector.HasFeature($"av1.frames={vector.FrameCount}")));
        Assert.Contains(vectors, vector => vector.FrameCount > 1);
    }

    [Fact]
    public void Av1VectorFrameSizesMatchTheirFeatures()
    {
        var vectors = LoadManifest().Av1Vectors!.Entries;
        Assert.All(vectors, vector =>
        {
            // The frame size only changes inside a stream with reference scaling, which the tag lists size by size
            var sizes = vector.Frames.Select(frame => $"{frame.Width.ToString(CultureInfo.InvariantCulture)}x{frame.Height.ToString(CultureInfo.InvariantCulture)}").Distinct(StringComparer.Ordinal).ToList();
            var tag = vector.Features.SingleOrDefault(feature => feature.StartsWith("av1.frame_sizes=", StringComparison.Ordinal));
            Assert.Equal(sizes.Count > 1 ? "av1.frame_sizes=" + string.Join(',', sizes) : null, tag);
        });
        Assert.Contains(vectors, vector => vector.HasFeatureKey("av1.frame_sizes"));
    }

    [Theory]
    [InlineData("av1.bitDepth=8")]
    [InlineData("av1.bitDepth=10")]
    [InlineData("av1.bitDepth=12")]
    [InlineData("av1.subsampling=4:0:0")]
    [InlineData("av1.subsampling=4:2:0")]
    [InlineData("av1.subsampling=4:2:2")]
    [InlineData("av1.subsampling=4:4:4")]
    [InlineData("av1.matrix=identity")]
    [InlineData("av1.lossless")]
    [InlineData("av1.still_picture")]
    [InlineData("av1.reduced_still_picture_header")]
    [InlineData("av1.superblock=64")]
    [InlineData("av1.superblock=128")]
    [InlineData("av1.tiles=2x2")]
    [InlineData("av1.tiles=4x1")]
    [InlineData("av1.tiles.spacing=uniform")]
    [InlineData("av1.tiles.spacing=explicit")]
    [InlineData("av1.tileGroups=2")]
    [InlineData("av1.using_qmatrix")]
    [InlineData("av1.delta_q_present")]
    [InlineData("av1.delta_lf_present")]
    [InlineData("av1.segmentation_enabled")]
    [InlineData("av1.allow_screen_content_tools")]
    [InlineData("av1.enable_filter_intra=0")]
    [InlineData("av1.enable_intra_edge_filter=0")]
    [InlineData("av1.disable_cdf_update")]
    [InlineData("av1.reduced_tx_set")]
    [InlineData("av1.tx_mode=largest")]
    [InlineData("av1.tx_mode=select")]
    [InlineData("av1.loop_filter")]
    [InlineData("av1.loop_filter_sharpness")]
    [InlineData("av1.cdef_bits=0")]
    [InlineData("av1.cdef_bits=1")]
    [InlineData("av1.cdef_bits=2")]
    [InlineData("av1.lr.y=wiener")]
    [InlineData("av1.lr.y=sgrproj")]
    [InlineData("av1.lr.y=switchable")]
    [InlineData("av1.lr.u=wiener")]
    [InlineData("av1.lr.u=sgrproj")]
    [InlineData("av1.lr.v=wiener")]
    [InlineData("av1.lr.v=sgrproj")]
    [InlineData("av1.lr.v=switchable")]
    [InlineData("av1.superres.denominator=9")]
    [InlineData("av1.superres.denominator=16")]
    [InlineData("av1.film_grain")]
    [InlineData("av1.film_grain.overlap")]
    [InlineData("av1.film_grain.chroma_scaling_from_luma")]
    [InlineData("av1.film_grain.clip_to_restricted_range")]
    [InlineData("av1.frames=3")]
    [InlineData("av1.frame_types=key")]
    [InlineData("av1.frame_types=inter")]
    [InlineData("av1.frame_types=intra_only")]
    [InlineData("av1.frame_types=switch")]
    [InlineData("av1.show_existing_frame")]
    [InlineData("av1.hidden_frames")]
    [InlineData("av1.reference_select")]
    [InlineData("av1.skip_mode_present")]
    [InlineData("av1.use_ref_frame_mvs")]
    [InlineData("av1.allow_warped_motion")]
    [InlineData("av1.is_motion_mode_switchable")]
    [InlineData("av1.force_integer_mv")]
    [InlineData("av1.gm=rotzoom")]
    [InlineData("av1.interpolation_filter=eighttap")]
    [InlineData("av1.interpolation_filter=sharp")]
    [InlineData("av1.interpolation_filter=switchable")]
    [InlineData("av1.interpolation_filter=bilinear")]
    [InlineData("av1.interpolation_filter=smooth")]
    [InlineData("av1.patched_header")]
    [InlineData("av1.hand_authored")]
    [InlineData("av1.enable_dual_filter")]
    [InlineData("av1.segmentation.features=5")]
    [InlineData("av1.segmentation.features=6")]
    [InlineData("av1.segmentation.features=7")]
    [InlineData("av1.film_grain.from_reference")]
    [InlineData("av1.delta_q.y_dc")]
    [InlineData("av1.lr.u=switchable")]
    [InlineData("av1.lr.unitSize=64")]
    [InlineData("av1.lr.unitSize=128")]
    [InlineData("av1.lr.unitSize=256")]
    [InlineData("av1.frames=30")]
    [InlineData("av1.primary_ref_frame=none")]
    [InlineData("av1.primary_ref_frame=set")]
    [InlineData("av1.error_resilient_mode")]
    [InlineData("av1.frame_id_numbers_present")]
    [InlineData("av1.disable_frame_end_update_cdf")]
    [InlineData("av1.frame_size_override_flag")]
    [InlineData("av1.found_ref")]
    [InlineData("av1.render_and_frame_size_different")]
    [InlineData("av1.allow_intrabc")]
    [InlineData("av1.segmentation.update_map=0")]
    [InlineData("av1.segmentation.update_map=1")]
    [InlineData("av1.segmentation.temporal_update")]
    [InlineData("av1.operating_points=2")]
    [InlineData("av1.operating_points=3")]
    [InlineData("av1.operating_points=4")]
    [InlineData("av1.spatial_layers=2")]
    [InlineData("av1.spatial_layers=3")]
    [InlineData("av1.spatial_layers=4")]
    [InlineData("av1.spatial_layers.present=1")]
    [InlineData("av1.spatial_layers.present=3")]
    public void Av1VectorsCoverFeature(string feature)
    {
        Assert.Contains(LoadManifest().Av1Vectors!.Entries, vector => vector.HasFeature(feature));
    }

    [Theory]
    [InlineData("4:0:0", 8)]
    [InlineData("4:0:0", 10)]
    [InlineData("4:0:0", 12)]
    [InlineData("4:2:0", 8)]
    [InlineData("4:2:0", 10)]
    [InlineData("4:2:0", 12)]
    [InlineData("4:2:2", 8)]
    [InlineData("4:2:2", 10)]
    [InlineData("4:2:2", 12)]
    [InlineData("4:4:4", 8)]
    [InlineData("4:4:4", 10)]
    [InlineData("4:4:4", 12)]
    public void Av1VectorsCoverEverySubsamplingAndBitDepth(string subsampling, int bitDepth)
    {
        // A lossy vector for every combination: the transforms, the quantizers and the filters depend on both
        Assert.Contains(LoadManifest().Av1Vectors!.Entries, vector => vector.Subsampling == subsampling && vector.BitDepth == bitDepth && !vector.HasFeature("av1.lossless"));
    }

    [Theory]
    [InlineData("av1.frame_types=inter", "av1.subsampling=4:0:0")]
    [InlineData("av1.frame_types=inter", "av1.subsampling=4:2:0")]
    [InlineData("av1.frame_types=inter", "av1.subsampling=4:2:2")]
    [InlineData("av1.frame_types=inter", "av1.subsampling=4:4:4")]
    [InlineData("av1.frame_types=inter", "av1.bitDepth=8")]
    [InlineData("av1.frame_types=inter", "av1.bitDepth=10")]
    [InlineData("av1.frame_types=inter", "av1.bitDepth=12")]
    [InlineData("av1.frame_types=inter", "av1.lossless")]
    [InlineData("av1.frame_types=inter", "av1.film_grain")]
    [InlineData("av1.frame_types=inter", "av1.superres.denominator=11")]
    [InlineData("av1.frame_types=inter", "av1.tiles=2x2")]
    [InlineData("av1.frame_types=inter", "av1.superblock=128")]
    [InlineData("av1.frame_types=inter", "av1.using_qmatrix")]
    [InlineData("av1.frame_types=inter", "av1.segmentation_enabled")]
    [InlineData("av1.frame_types=inter", "av1.allow_screen_content_tools")]
    [InlineData("av1.frame_types=inter", "av1.allow_intrabc")]
    [InlineData("av1.frame_types=inter", "av1.lr.y=wiener")]
    [InlineData("av1.frame_types=inter", "av1.lr.y=sgrproj")]
    [InlineData("av1.frame_types=inter", "av1.cdef_bits=0")]
    [InlineData("av1.show_existing_frame", "av1.film_grain")]
    [InlineData("av1.show_existing_frame", "av1.found_ref")]
    [InlineData("av1.frame_types=switch", "av1.hidden_frames")]
    [InlineData("av1.allow_intrabc", "av1.subsampling=4:4:4")]
    [InlineData("av1.allow_intrabc", "av1.bitDepth=10")]
    [InlineData("av1.allow_intrabc", "av1.bitDepth=12")]
    [InlineData("av1.frame_types=inter", "av1.delta_q.y_dc")]
    [InlineData("av1.frame_types=inter", "av1.enable_dual_filter")]
    [InlineData("av1.frame_types=inter", "av1.film_grain.from_reference")]
    [InlineData("av1.frame_types=inter", "av1.lr.u=wiener")]
    [InlineData("av1.superres.denominator=12", "av1.frame_size_override_flag")]
    [InlineData("av1.interpolation_filter=bilinear", "av1.hidden_frames")]
    [InlineData("av1.interpolation_filter=bilinear", "av1.subsampling=4:4:4")]
    [InlineData("av1.interpolation_filter=bilinear", "av1.subsampling=4:2:2")]
    [InlineData("av1.interpolation_filter=bilinear", "av1.subsampling=4:0:0")]
    [InlineData("av1.interpolation_filter=bilinear", "av1.bitDepth=12")]
    [InlineData("av1.spatial_layers=2", "av1.subsampling=4:0:0")]
    [InlineData("av1.spatial_layers=2", "av1.subsampling=4:2:2")]
    [InlineData("av1.spatial_layers=2", "av1.matrix=identity")]
    [InlineData("av1.spatial_layers=2", "av1.frames=2")]
    [InlineData("av1.spatial_layers=3", "av1.bitDepth=10")]
    [InlineData("av1.spatial_layers=4", "av1.bitDepth=12")]
    [InlineData("av1.spatial_layers=4", "av1.bitDepth=8")]
    [InlineData("av1.spatial_layers=2", "av1.render_and_frame_size_different")]
    public void Av1VectorsCoverFeatureCombination(string first, string second)
    {
        Assert.Contains(LoadManifest().Av1Vectors!.Entries, vector => vector.HasFeature(first) && vector.HasFeature(second));
    }

    [Fact]
    public void Av1InterVectorsCoverLongAndLargeStreams()
    {
        // Whole groups of pictures from both encoders, and frames of several superblocks in both directions
        var inter = LoadManifest().Av1Vectors!.Entries.Where(vector => vector.HasFeature("av1.frame_types=inter")).ToList();
        foreach (var encoder in new[] { "libaom-av1", "libsvtav1" })
        {
            Assert.Contains(inter, vector => vector.FrameCount >= 16 && vector.Provenance.Commands![0].Contains(" -c:v " + encoder + " ", StringComparison.Ordinal) && vector.HasFeature("av1.hidden_frames"));
            Assert.Contains(inter, vector => vector.Frames[0].Width >= 352 && vector.Frames[0].Height >= 288 && vector.Provenance.Commands![0].Contains(" -c:v " + encoder + " ", StringComparison.Ordinal));
        }

        Assert.Contains(inter, vector => vector.FrameCount >= 30);
        Assert.Contains(inter, vector => vector.HasFeature("av1.superblock=128") && vector.HasFeature("av1.tiles=2x2"));

        // Reference scaling: streams whose frames take five sizes or more
        Assert.Contains(inter, vector => vector.Frames.Select(frame => (frame.Width, frame.Height)).Distinct().Count() >= 5);
    }

    private static FixtureManifest LoadManifest() => FixtureManifestLoader.Load(FixtureRoot.GetDirectory() / FixtureRoot.ManifestFileName);

    private static JsonDocument LoadSchema() => JsonDocument.Parse(File.ReadAllText(FixtureRoot.GetDirectory() / FixtureRoot.SchemaFileName));

    /// <summary>The manifest as the corpus generator reads it: objects, lists and texts (the other values are not paths).</summary>
    private static object? ToGeneratorValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new Obj();
                foreach (var property in element.EnumerateObject())
                {
                    obj[property.Name] = ToGeneratorValue(property.Value);
                }

                return obj;
            case JsonValueKind.Array:
                return element.EnumerateArray().Select(ToGeneratorValue).ToList();
            case JsonValueKind.String:
                return element.GetString();
            default:
                return null;
        }
    }

    private static IEnumerable<string> Enum(JsonElement defs, string definition, string property)
        => defs.GetProperty(definition).GetProperty("properties").GetProperty(property).GetProperty("enum").EnumerateArray().Select(item => item.GetString()!);

    private static void AssertSameSet(IEnumerable<string> expected, IEnumerable<string> actual)
        => Assert.Equal(expected.Order(StringComparer.Ordinal).ToArray(), actual.Order(StringComparer.Ordinal).ToArray());
}
