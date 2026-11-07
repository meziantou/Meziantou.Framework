using System.Globalization;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Heif;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.InteropTests;

// The image sequences of the HEIC suite.
public sealed partial class HeicDecoderInteropTests
{
    /// <summary>
    /// Image sequences written by <c>heif-enc -S</c> from numbered PNG files: a track of HEVC samples (and a track of alpha
    /// planes), next to the first image stored as an item. The frame count, the exact durations and the play count are read
    /// through <c>Image.Load</c>, <c>Image.Identify</c> and <c>Image.OpenReader</c>; every image is compared with the planes
    /// of the reference decoders and with <c>heif-dec -S</c>. The sequences that use inter prediction are rejected while
    /// <see cref="InterPredictionIsDecoded"/> is <see langword="false"/>.
    /// </summary>
    [Theory]
    [MemberData(nameof(SequenceCases))]
    public async Task ImageSequencesDecodeAsTheReferences(string name)
    {
        var heif = await RequiredTools.LibHeifAsync();
        var item = Sequences[name];

        // heif-enc reads every file of the directory whose name is the name of the first one with another number
        var directory = ArtifactPath(name + ".frames");
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        Directory.CreateDirectory(directory);
        var sources = new List<RawPixelBuffer>();
        FullPath first = default;
        for (var i = 0; i < item.FrameCount; i++)
        {
            sources.Add(CreateSource(item.Source, item.Width, item.Height, seed: StableSeed(name), frame: i));
            var framePath = await SaveSourceAsync(string.Create(CultureInfo.InvariantCulture, $"frame-{i + 1:D3}"), sources[i], directory);
            first = i == 0 ? framePath : first;
        }

        var path = ArtifactPath(name + ".heic");
        await heif.EncodeAsync(["-S", .. item.Options, first], path, XunitCancellationToken);
        if (item.Durations is { } durations)
        {
            // heif-enc gives one duration to every sample: the file is written again with other durations, and the edit list
            // and the duration of the tracks that give the same number of plays
            Assert.False(item.UsesInterPrediction, $"{name}: the builder of the test harness does not copy the sync sample table and the composition offsets of a track.");
            var builder = HeifFileBuilder.From(await File.ReadAllBytesAsync(path, XunitCancellationToken));
            var plays = ReferenceHeif.Parse(await File.ReadAllBytesAsync(path, XunitCancellationToken)).ColorTrack!.TotalPlays;
            foreach (var track in builder.Tracks)
            {
                Assert.HasCount(durations.Length, track.Samples);
                Assert.Equal(builder.MovieTimescale, track.Timescale);
                for (var i = 0; i < durations.Length; i++)
                {
                    track.Samples[i] = (track.Samples[i].Data, (uint)durations[i]);
                }

                var total = (ulong)durations.Sum();
                if (track.EditList is { } edits)
                {
                    var edit = Assert.Single(edits.Entries);
                    edits.Entries[0] = (total, edit.MediaTime, edit.MediaRateInteger, edit.MediaRateFraction);
                    track.Duration = plays is { } count ? total * (ulong)count : ulong.MaxValue;
                }
                else
                {
                    track.Duration = total;
                }
            }

            await File.WriteAllBytesAsync(path, builder.Build(), XunitCancellationToken);
        }

        await AssertSequenceAsync(heif, path, name, item.Comparison, item.UsesInterPrediction, item.FrameCount, item.SourceIsPreserved ? sources : null, item.Durations);
    }

    /// <summary>
    /// The layouts of a sequence that heif-enc does not write: a file of heif-enc (intra pictures and an alpha track) is
    /// laid out differently with <see cref="HeifFileBuilder"/> and must decode to the pixels, the durations and the play
    /// count of the original. libheif reads these layouts too, except those that <paramref name="readByLibHeif"/> marks:
    /// it knows neither the <c>hev1</c> sample entries, nor the <c>co64</c> box, nor the <c>stz2</c> box.
    /// </summary>
    [Theory]
    [InlineData("hev1-sample-entries", false)]
    [InlineData("chunks-in-reverse-order", true)]
    [InlineData("large-chunk-offsets", false)]
    [InlineData("compact-sample-sizes", false)]
    [InlineData("mdat-before-moov", true)]
    [InlineData("moov-before-meta", true)]
    [InlineData("item-data-after-samples", true)]
    [InlineData("sequence-only", true)]
    public async Task SequenceLayoutsDecodeAsTheOriginalFile(string layout, bool readByLibHeif)
    {
        var heif = await RequiredTools.LibHeifAsync();
        var name = "sequence-layout-" + layout;
        var directory = ArtifactPath(name + ".frames");
        Directory.CreateDirectory(directory);
        FullPath first = default;
        for (var i = 0; i < 4; i++)
        {
            var framePath = await SaveSourceAsync(string.Create(CultureInfo.InvariantCulture, $"frame-{i + 1:D3}"), CreateSource(SourceKind.Rgba8, 96, 64, seed: 41, frame: i), directory);
            first = i == 0 ? framePath : first;
        }

        var path = ArtifactPath(name + "-source.heic");
        await heif.EncodeAsync(["-S", "-q", "60", "-p", "chroma=444", "--gop-structure", "intra-only", "--timebase", "1000", "--duration", "40", "--repetitions", "3", first], path, XunitCancellationToken);
        var original = await AssertSequenceAsync(heif, path, name + "-source", HeifDecComparison.Matrix, usesInterPrediction: false, frameCount: 4, preservedSources: null, expectedDurations: null);

        var builder = HeifFileBuilder.From(await File.ReadAllBytesAsync(path, XunitCancellationToken));
        Assert.HasCount(2, builder.Tracks);
        switch (layout)
        {
            case "hev1-sample-entries":
                // The parameter sets of an 'hev1' sample entry may be in the samples; here they stay in the record
                foreach (var track in builder.Tracks)
                {
                    track.SampleEntryType = "hev1";
                }

                break;

            case "chunks-in-reverse-order":
                // Chunks of two samples, the last one first in the file
                builder.ChunksInReverseOrder = true;
                foreach (var track in builder.Tracks)
                {
                    track.SamplesPerChunk = 2;
                }

                break;

            case "compact-sample-sizes":
                foreach (var track in builder.Tracks)
                {
                    track.SampleSizeBox = "stz2";
                }

                break;

            case "large-chunk-offsets":
                // A chunk per sample, with 64-bit offsets
                foreach (var track in builder.Tracks)
                {
                    track.SamplesPerChunk = 1;
                    track.ChunkOffsetBox = "co64";
                }

                break;

            case "mdat-before-moov":
                builder.Layout.Clear();
                builder.Layout.Add(HeifFileBuilder.FileTypeBox);
                builder.Layout.Add(HeifFileBuilder.MediaDataBox);
                builder.Layout.Add(HeifFileBuilder.MetaBox);
                builder.Layout.Add(HeifFileBuilder.MovieBox);
                break;

            case "moov-before-meta":
                builder.Layout.Clear();
                builder.Layout.Add(HeifFileBuilder.FileTypeBox);
                builder.Layout.Add(HeifFileBuilder.MovieBox);
                builder.Layout.Add(HeifFileBuilder.MetaBox);
                builder.Layout.Add(HeifFileBuilder.MediaDataBox);
                break;

            case "item-data-after-samples":
                builder.ItemDataFollowsSamples = true;
                break;

            case "sequence-only":
                // No 'meta' box: the file is an image sequence and nothing else
                builder.Layout.Remove(HeifFileBuilder.MetaBox);
                builder.Items.Clear();
                builder.References.Clear();
                builder.PrimaryItemId = null;
                builder.MajorBrand = "hevx";
                builder.CompatibleBrands.Clear();
                builder.CompatibleBrands.Add("msf1");
                builder.CompatibleBrands.Add("hevx");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(layout), layout, "Unknown layout.");
        }

        var variantPath = ArtifactPath(name + ".heic");
        var bytes = builder.Build();
        await File.WriteAllBytesAsync(variantPath, bytes, XunitCancellationToken);
        var reader = ReferenceHeif.Parse(bytes);
        Assert.Equal(layout == "hev1-sample-entries" ? "hev1" : "hvc1", reader.ColorTrack?.SampleEntryType);
        Assert.Equal(layout == "large-chunk-offsets" ? "co64" : "stco", reader.ColorTrack?.ChunkOffsetBox);
        Assert.Equal(layout == "compact-sample-sizes" ? "stz2" : "stsz", reader.ColorTrack?.SampleSizeBox);
        Assert.Equal(layout != "sequence-only", reader.HasMeta);
        if (readByLibHeif)
        {
            var variant = await AssertSequenceAsync(heif, variantPath, name, HeifDecComparison.Matrix, usesInterPrediction: false, frameCount: 4, preservedSources: null, expectedDurations: null);
            for (var i = 0; i < original.Count; i++)
            {
                AssertPixels(original[i], variant[i], ColorBound.Exact, string.Create(CultureInfo.InvariantCulture, $"{name} frame {i}: the pixels of the file laid out differently vs the pixels of the original"));
            }

            return;
        }

        // libheif does not read the file: the library must read it as it reads the original
        await Assert.ThrowsAsync<InvalidOperationException>(() => heif.GetInfoAsync(variantPath, XunitCancellationToken));
        using var expected = Image.Load(path);
        using var image = Image.Load(variantPath);
        Assert.Equal(ImageFormat.Heic, image.Metadata.SourceFormat);
        Assert.Equal((expected.PixelFormat, expected.Size, expected.Frames.Count, expected.Animation?.TotalPlays), (image.PixelFormat, image.Size, image.Frames.Count, image.Animation?.TotalPlays));
        Assert.Equal(expected.Frames.Select(frame => frame.Metadata.Duration), image.Frames.Select(frame => frame.Metadata.Duration));
        for (var i = 0; i < original.Count; i++)
        {
            AssertPixels(original[i], ToRgba16(ImageSnapshots.CaptureFrame(image.Frames[i])), ColorBound.Exact, string.Create(CultureInfo.InvariantCulture, $"{name} frame {i}: the pixels of the file laid out differently vs the pixels of the original"));
        }

        var scanned = Image.Identify(variantPath, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.Equal<(ImageFormat, int?, int?)>((ImageFormat.Heic, 4, 3), (scanned.Format, scanned.FrameCount, scanned.Animation?.TotalPlays));
    }

    /// <summary>
    /// A sequence of pictures that x265 does not code as they are (65x47): heif-enc pads the pictures to 66x64, as it does
    /// for a still image, but describes the track with the size of the source and gives it no clean aperture. Nothing in
    /// the file says which part of the pictures is the image: <c>heif-dec -S</c> returns the padded pictures, and the
    /// library rejects a track whose pictures do not have its size.
    /// </summary>
    [Fact]
    public async Task SequenceWhosePicturesDoNotHaveTheSizeOfTheTrackIsRejected()
    {
        var heif = await RequiredTools.LibHeifAsync();
        const string Name = "sequence-65x47";
        var directory = ArtifactPath(Name + ".frames");
        Directory.CreateDirectory(directory);
        FullPath first = default;
        for (var i = 0; i < 3; i++)
        {
            var framePath = await SaveSourceAsync(string.Create(CultureInfo.InvariantCulture, $"frame-{i + 1:D3}"), CreateSource(SourceKind.Rgb8, 65, 47, seed: 43, frame: i), directory);
            first = i == 0 ? framePath : first;
        }

        var path = ArtifactPath(Name + ".heic");
        await heif.EncodeAsync(["-S", "-L", "--gop-structure", "intra-only", first], path, XunitCancellationToken);
        var reader = ReferenceHeif.Parse(await File.ReadAllBytesAsync(path, XunitCancellationToken));
        var track = reader.ColorTrack!;
        Assert.Equal((65, 47), (track.Width, track.Height));
        var sequence = ReadSequenceParameterSet(track.HevcConfiguration!, Name);
        Assert.Equal((66, 64), (sequence.CropWidth, sequence.CropHeight));
        var references = await heif.DecodeSequenceAsync(path, bilinearChroma: true, XunitCancellationToken);
        Assert.Equal((3, 66, 64), (references.Count, references[0].Image.Width, references[0].Image.Height));

        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(path).Dispose());
        Assert.Equal(ImageFormat.Heic, exception.Format);
    }

    /// <summary>
    /// A sequence through <c>Load</c>, <c>Identify</c> and <c>OpenReader</c>: frame count, exact durations, play count, and
    /// every image against the planes of the reference decoders and against heif-dec.
    /// </summary>
    /// <returns>The pixels of the images of the library; none when it rejects the sequence because it uses inter prediction.</returns>
    private static async Task<List<RawPixelBuffer>> AssertSequenceAsync(LibHeifTool heif, FullPath path, string name, HeifDecComparison comparison, bool usesInterPrediction, int frameCount, List<RawPixelBuffer>? preservedSources, int[]? expectedDurations)
    {
        var bytes = await File.ReadAllBytesAsync(path, XunitCancellationToken);
        var reader = ReferenceHeif.Parse(bytes);
        var info = await heif.GetInfoAsync(path, XunitCancellationToken);
        var track = reader.ColorTrack ?? throw new InvalidDataException($"{name}: the file has no track of images.");
        var alphaTrack = reader.AlphaTrack;
        Assert.Equal("image/heic-sequence", info.MimeType);
        var infoTrack = Assert.Single(info.Tracks, candidate => candidate.Handler == "pict");
        Assert.Equal<(int, int)?>((track.Width, track.Height), infoTrack.Resolution);
        Assert.Equal(alphaTrack is not null, info.Tracks.Any(candidate => candidate.Handler == "auxv"));
        Assert.Equal(frameCount, track.Samples.Count);

        // The references decode the stream, even when the library does not: the planes of a case are ready for the day it does
        var color = await DecodeTrackAsync(name + ".color", reader, track);
        var alpha = alphaTrack is null ? null : await DecodeTrackAsync(name + ".alpha", reader, alphaTrack);
        var sliceTypes = color[0].Signal.SliceTypes;
        Assert.Equal(usesInterPrediction, sliceTypes.Any(type => type != 2), message: $"{name}: the slice types of the stream are {string.Join(", ", sliceTypes)}.");

        // The images, in presentation order: an image lasts until the next one
        var order = track.PresentationOrder;
        var durations = order.Select(entry => new FrameDuration(entry.Duration, track.Timescale)).ToList();
        if (expectedDurations is not null)
        {
            Assert.Equal(expectedDurations.Select(duration => (long)duration), order.Select(entry => entry.Duration));
        }

        // The play count. libheif plays a track without edit list once; the library documents that such a track repeats
        // forever, the rule of the AV1 Image File Format that it applies to every file of the container
        var totalPlays = track.TotalPlays;
        if (track.EditList is null)
        {
            Assert.Null(totalPlays);
            Assert.Equal<int?>(1, infoTrack.Repetitions);
        }
        else
        {
            Assert.Equal(infoTrack.Repetitions, totalPlays, message: $"{name}: heif-info does not print the play count that the edit list gives.");
        }

        var (matrix, fullRange) = ResolveColor(track.Nclx, color[0].Signal);
        var cicp = ExpectedCicp(track.Nclx, color[0].Signal);
        var pixelFormat = ExpectedPixelFormat(color[0].BitDepth, color[0].IsMonochrome, alpha is not null);
        var size = new Size(track.Width, track.Height);
        var crop = (X: 0, Y: 0, track.Width, track.Height);

        // A header identification does not count the frames; a full scan does. Neither decodes a picture
        var header = Image.Identify(path, new ImageIdentifyOptions { Mode = ImageIdentifyMode.Header });
        var scanned = Image.Identify(path, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        foreach (var identified in new[] { header, scanned })
        {
            Assert.Equal(ImageFormat.Heic, identified.Format);
            Assert.Equal(size, identified.Size);
            Assert.Equal(pixelFormat, identified.PixelFormat);
            Assert.Equal(color[0].BitDepth, identified.BitsPerComponent);
            Assert.Equal<bool?>(alpha is not null, identified.MayHaveTransparency);
            Assert.Equal(cicp, identified.Metadata.Cicp);
            Assert.Equal(ExifOrientation.TopLeft, identified.Metadata.Orientation);
        }

        Assert.Equal<int?>(frameCount, scanned.FrameCount);
        Assert.Equal<bool?>(true, scanned.IsAnimated);
        Assert.Equal(totalPlays, scanned.Animation?.TotalPlays);

        // The pixels the planes convert to, and the images of heif-dec, which come in presentation order too, each with the
        // duration of its sample: the image of heif-dec that an expected image resembles most is the one of its rank
        Assert.Equal(comparison, Classify(color[0], matrix, fullRange, crop, isSequence: true), message: $"{name}: the kind of comparison with heif-dec that the case declares is not the one its file calls for.");
        var expected = new List<RawPixelBuffer>();
        for (var i = 0; i < frameCount; i++)
        {
            expected.Add(ConvertToPixels(color[i], alpha?[i], matrix, fullRange, crop, ChromaModel.Declared));
        }

        var references = await heif.DecodeSequenceAsync(path, bilinearChroma: true, XunitCancellationToken);
        Assert.Equal(frameCount, references.Count);
        Assert.Equal(order.Select(entry => (long)track.Samples[entry.SampleIndex].Duration), references.Select(reference => reference.Duration));
        for (var i = 0; i < frameCount; i++)
        {
            Assert.Equal((track.Width, track.Height), (references[i].Image.Width, references[i].Image.Height));
            var decoded = references[i].Image.GetPixels(references[i].Image.FileBitDepth == 8 ? 8 : color[i].BitDepth);
            var closest = Enumerable.Range(0, frameCount).MinBy(candidate => MeasurePixels(expected[candidate], decoded).Mean);
            Assert.Equal(i, closest, message: $"{name}: the image {i} of heif-dec is the image {closest} of the reference decoders.");
        }

#pragma warning disable CS0162 // Unreachable code: one of the two branches, by the value of the constant
        if (usesInterPrediction && !InterPredictionIsDecoded)
        {
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(path).Dispose());
            Assert.Equal(ImageFormat.Heic, exception.Format);
            Assert.Contains("inter prediction", exception.Message, StringComparison.Ordinal);
            WriteOutput($"{name}: {frameCount} frames, slice types {string.Join(' ', sliceTypes)}, presentation order {string.Join(' ', order.Select(entry => entry.SampleIndex))}: rejected, inter prediction is not decoded ({exception.Message})");
            return [];
        }
#pragma warning restore CS0162

        using var image = Image.Load(path);
        Assert.Equal(ImageFormat.Heic, image.Metadata.SourceFormat);
        Assert.Equal(pixelFormat, image.PixelFormat);
        Assert.Equal(size, image.Size);
        Assert.Null(image.PosterFrame);
        Assert.Equal(frameCount, image.Frames.Count);
        Assert.Equal(durations, image.Frames.Select(frame => frame.Metadata.Duration));
        Assert.NotNull(image.Animation);
        Assert.Equal(totalPlays, image.Animation.TotalPlays);
        Assert.Equal(cicp, image.Metadata.Cicp);
        Assert.Equal(ExifOrientation.TopLeft, image.Metadata.Orientation);
        var oracleBound = ColorBound.Oracle(color[0], alpha?[0], matrix, fullRange);
        var frames = new List<RawPixelBuffer>();
        var oracleMaximum = 0;
        var report = "";

        // libde265, the decoder of heif-dec, is a reference only when it decodes the planes of the other decoders
        var libDe265Agrees = color[0].Decoders.Contains(HevcReferenceDecoder.Dec265, StringComparison.Ordinal) && alpha?[0].Decoders.Contains(HevcReferenceDecoder.Dec265, StringComparison.Ordinal) is not false;
        for (var i = 0; i < frameCount; i++)
        {
            var frameName = string.Create(CultureInfo.InvariantCulture, $"{name} frame {i}");
            var actual = ToRgba16(ImageSnapshots.CaptureFrame(image.Frames[i]));
            frames.Add(actual);
            oracleMaximum = Math.Max(oracleMaximum, AssertPixels(expected[i], actual, oracleBound, $"{frameName}: library vs the planes of {color[i].Decoders} converted by the oracle").Max);
            if (preservedSources is not null)
            {
                AssertPixels(ToStoredPixels(preservedSources[i], color[i].BitDepth), actual, ColorBound.Exact, $"{frameName}: library vs source");
            }

            report = libDe265Agrees
                ? CompareWithHeifDec(frameName, comparison, heif, color[i], alpha?[i], matrix, fullRange, crop, references[i].Image, actual, isSequence: true)
                : "heif-dec is not compared, libde265 does not decode the planes of the other decoders";
        }

        WriteOutput(string.Create(CultureInfo.InvariantCulture, $"{name}: {frameCount} frames, slice types {string.Join(' ', sliceTypes)}, presentation order {string.Join(' ', order.Select(entry => entry.SampleIndex))}, {info.ColorSpace} {color[0].BitDepth}-bit matrix {matrix} {(fullRange ? "full" : "limited")}{(alpha is not null ? " alpha" : "")}: library vs oracle ({color[0].Decoders}) max {oracleMaximum} (bound {oracleBound}); last frame: {report}"));

        // The streaming reader, on a stream that cannot seek: one frame at a time
        await using var stream = new ForwardOnlyStream(bytes);
        if (color[0].BitDepth == 8)
        {
            await AssertReaderAsync<Rgba32>(stream, expected, durations, totalPlays, oracleBound, name);
        }
        else
        {
            await AssertReaderAsync<Rgba64>(stream, expected, durations, totalPlays, oracleBound, name);
        }

        return frames;
    }

    private static async Task AssertReaderAsync<TPixel>(Stream stream, List<RawPixelBuffer> expected, List<FrameDuration> durations, int? totalPlays, int maxColorError, string name)
        where TPixel : unmanaged
    {
        await using var reader = await Image.OpenReaderAsync<TPixel>(stream, new ImageReaderOptions { LeaveOpen = true }, XunitCancellationToken);
        Assert.Equal(ImageFormat.Heic, reader.Info.Format);
        Assert.Equal(totalPlays, reader.Info.Animation?.TotalPlays);
        Assert.Null(await reader.ReadPosterFrameAsync(XunitCancellationToken));
        for (var i = 0; i < expected.Count; i++)
        {
            using var frame = await reader.ReadFrameAsync(XunitCancellationToken);
            Assert.NotNull(frame, $"{name}: the reader ends before frame {i}.");
            Assert.Equal(durations[i], frame.Frames[0].Metadata.Duration);
            AssertPixels(expected[i], ToRgba16(ImageSnapshots.CaptureFrame(frame.Frames[0])), maxColorError, $"{name} frame {i}: reader vs the planes converted by the oracle");
        }

        Assert.Null(await reader.ReadFrameAsync(XunitCancellationToken));
        Assert.Equal(expected.Count, reader.FramesRead);
    }
}
