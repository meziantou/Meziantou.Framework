using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Heif;
using Meziantou.Framework.Imaging.TestHarness.Hevc;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.InteropTests;

// The references of the HEIC suite that do not come from libheif: the planes that independent HEVC decoders give for the
// streams of a file, and their conversion to pixels by the oracle of the test harness.
public sealed partial class HeicDecoderInteropTests
{
    // The reference decoders, located once: locating one runs it to check its version
    private static readonly ConcurrentDictionary<string, HevcReferenceDecoder> ReferenceDecoders = new(StringComparer.Ordinal);

    /// <summary>
    /// The video signal description of the video usability information of a sequence parameter set (Rec. ITU-T H.265, clause
    /// E.2.1), as FFmpeg's <c>trace_headers</c> bitstream filter prints it: the library is not involved.
    /// </summary>
    /// <param name="HasVideoSignalType"><c>video_signal_type_present_flag</c>.</param>
    /// <param name="FullRange"><c>video_full_range_flag</c>; <see langword="false"/> when it is absent.</param>
    /// <param name="HasColorDescription"><c>colour_description_present_flag</c>.</param>
    /// <param name="ColorPrimaries"><c>colour_primaries</c>; 2 (unspecified) when it is absent.</param>
    /// <param name="TransferCharacteristics"><c>transfer_characteristics</c>; 2 (unspecified) when it is absent.</param>
    /// <param name="MatrixCoefficients"><c>matrix_coeffs</c>; 2 (unspecified) when it is absent.</param>
    /// <param name="ChromaSampleLocType"><c>chroma_sample_loc_type_top_field</c>; 0 when it is absent, which is what its absence means.</param>
    /// <param name="SliceTypes">The <c>slice_type</c> of every slice segment of the stream that has one, in order: 0 (B), 1 (P) or 2 (I).</param>
    internal sealed record VideoSignal(bool HasVideoSignalType, bool FullRange, bool HasColorDescription, int ColorPrimaries, int TransferCharacteristics, int MatrixCoefficients, int ChromaSampleLocType, IReadOnlyList<int> SliceTypes);

    /// <summary>
    /// The planes of a picture, or of the image that the pictures of the tiles of a grid form, as two reference decoders give
    /// them, byte for byte.
    /// </summary>
    /// <param name="Width">The width of the luma plane: the width of the picture, or of the image of a grid.</param>
    /// <param name="Height">The height of the luma plane.</param>
    /// <param name="BitDepth">The bit depth of the samples.</param>
    /// <param name="ChromaFormatIdc"><c>chroma_format_idc</c>: 0 for monochrome, 1 for 4:2:0, 2 for 4:2:2, 3 for 4:4:4.</param>
    /// <param name="Y">The first plane.</param>
    /// <param name="U">The second plane; empty for a monochrome picture.</param>
    /// <param name="V">The third plane; empty for a monochrome picture.</param>
    /// <param name="Signal">The video signal description of the stream.</param>
    /// <param name="Decoders">The reference decoders that agree on the planes.</param>
    internal sealed record ReferencePlanes(int Width, int Height, int BitDepth, int ChromaFormatIdc, ushort[] Y, ushort[] U, ushort[] V, VideoSignal Signal, string Decoders)
    {
        public bool IsMonochrome => ChromaFormatIdc is 0;

        public int SubsamplingX => ChromaFormatIdc is 1 or 2 ? 1 : 0;

        public int SubsamplingY => ChromaFormatIdc is 1 ? 1 : 0;
    }

    /// <summary>The position of the chroma samples that a conversion assumes, and whether it crops the planes before it upsamples them.</summary>
    internal enum ChromaModel
    {
        /// <summary>
        /// The position the stream declares (<see cref="ReferenceYuvConverter.GetHevcChromaSiting"/>); the chroma planes of the
        /// whole image are upsampled, then the clean aperture crops the pixels: the samples that the aperture leaves out are
        /// still the neighbors of the ones it keeps. It is the conversion of the library.
        /// </summary>
        Declared,

        /// <summary>
        /// Chroma samples centered between the luma samples, whatever the stream declares, in planes that the clean aperture
        /// crops first: the samples of the edges of the cropped planes are replicated. It is the conversion of <c>heif-dec -C bilinear</c>.
        /// </summary>
        LibHeif,

        /// <summary>
        /// Chroma samples that are replicated instead of interpolated: the conversion of <c>heif-dec -S</c>, which ignores
        /// <c>-C bilinear</c> for the images of a sequence.
        /// </summary>
        LibHeifSequence,
    }

    /// <summary>
    /// Decodes the HEVC stream of a coded item, or of every tile of a grid item, with the reference decoders. The streams of
    /// the tiles are decoded as one stream of independent pictures.
    /// </summary>
    internal static async Task<ReferencePlanes> DecodeItemAsync(string name, ReferenceHeif heif, ReferenceHeif.Item item)
    {
        var isGrid = item.Type == "grid";
        var tiles = isGrid ? heif.GetGridTiles(item) : [item];
        var (rows, columns) = isGrid ? (heif.ReadGridDescriptor(item).Rows, heif.ReadGridDescriptor(item).Columns) : (1, 1);
        Assert.Equal(rows * columns, tiles.Count, message: $"{name}: the grid does not have one tile per cell.");
        using var stream = new MemoryStream();
        ReferenceHevcSequence? format = null;
        foreach (var tile in tiles)
        {
            Assert.Equal("hvc1", tile.Type, message: $"{name}: a coded item is not an HEVC image.");
            var configuration = tile.HevcConfiguration ?? throw new InvalidDataException($"{name}: the item {tile.Id} has no 'hvcC' property.");
            var sequence = ReadSequenceParameterSet(configuration, name);

            // The picture has the size that the 'ispe' property declares, and every tile has the format of the first one
            Assert.Equal<(long, long)?>((sequence.CropWidth, sequence.CropHeight), tile.Size, message: $"{name}: the conformance window of the item {tile.Id} is not its 'ispe' property.");
            Assert.Equal((configuration.ChromaFormat, configuration.BitDepthLuma), (sequence.ChromaFormatIdc, sequence.BitDepthLuma), message: $"{name}: the configuration record of the item {tile.Id} does not describe its sequence parameter set.");
            format ??= sequence;
            Assert.Equal(format, sequence, message: $"{name}: the tiles of the grid do not have the same format.");
            stream.Write(configuration.ToByteStream(heif.ReadItemData(tile)));
        }

        var (pictures, signal, decoders) = await DecodeStreamAsync(name, stream.ToArray(), format!, tiles.Count);
        if (!isGrid)
            return new ReferencePlanes(format!.CropWidth, format.CropHeight, format.BitDepthLuma, format.ChromaFormatIdc, pictures[0][0], pictures[0][1], pictures[0][2], signal, decoders);

        // The tiles, in row-major order, without overlap; the reconstructed image is the top-left part of this canvas that
        // has the size of the grid (ISO/IEC 23008-12, section 6.6.2.3.1): the samples of the tiles of the last row and
        // column that exceed it are not samples of the image, and no chroma sample is upsampled from them
        var (tileWidth, tileHeight) = (format!.CropWidth, format.CropHeight);
        var descriptor = heif.ReadGridDescriptor(item);
        var (width, height) = ((int)descriptor.Width, (int)descriptor.Height);
        Assert.True(width <= columns * tileWidth && width > (columns - 1) * tileWidth && height <= rows * tileHeight && height > (rows - 1) * tileHeight, $"{name}: the tiles of the grid do not cover its image, or a row or a column of tiles is outside of it.");
        var planes = new ushort[3][];
        for (var plane = 0; plane < 3; plane++)
        {
            var isChroma = plane > 0;
            if (isChroma && format.ChromaFormatIdc is 0)
            {
                planes[plane] = [];
                continue;
            }

            var (subX, subY) = isChroma ? (format.SubWidthC, format.SubHeightC) : (1, 1);
            Assert.Equal((0, 0), (tileWidth % subX, tileHeight % subY), message: $"{name}: the size of the tiles is not a whole number of chroma samples.");
            var (planeTileWidth, planeTileHeight) = (tileWidth / subX, tileHeight / subY);
            var (planeWidth, planeHeight) = ((width + subX - 1) / subX, (height + subY - 1) / subY);
            planes[plane] = new ushort[planeWidth * planeHeight];
            for (var tile = 0; tile < tiles.Count; tile++)
            {
                var (x, y) = ((tile % columns) * planeTileWidth, (tile / columns) * planeTileHeight);
                var visibleWidth = Math.Min(planeTileWidth, planeWidth - x);
                for (var row = 0; row < Math.Min(planeTileHeight, planeHeight - y); row++)
                {
                    pictures[tile][plane].AsSpan(row * planeTileWidth, visibleWidth).CopyTo(planes[plane].AsSpan(((y + row) * planeWidth) + x));
                }
            }
        }

        return new ReferencePlanes(width, height, format.BitDepthLuma, format.ChromaFormatIdc, planes[0], planes[1], planes[2], signal, decoders);
    }

    /// <summary>
    /// Decodes the samples of a track with the reference decoders: one stream, in decoding order, whose pictures the decoders
    /// output in presentation order.
    /// </summary>
    internal static async Task<IReadOnlyList<ReferencePlanes>> DecodeTrackAsync(string name, ReferenceHeif heif, ReferenceHeif.Track track)
    {
        var configuration = track.HevcConfiguration ?? throw new InvalidDataException($"{name}: the track {track.Id} has no 'hvcC' box.");
        var format = ReadSequenceParameterSet(configuration, name);
        Assert.Equal((track.Width, track.Height), (format.CropWidth, format.CropHeight), message: $"{name}: the conformance window of the track {track.Id} is not the size of its sample entry.");
        using var stream = new MemoryStream();
        for (var i = 0; i < track.Samples.Count; i++)
        {
            stream.Write(configuration.ToByteStream(heif.ReadSample(track.Samples[i]), includeParameterSets: i == 0));
        }

        var (pictures, signal, decoders) = await DecodeStreamAsync(name, stream.ToArray(), format, track.Samples.Count);
        return [.. pictures.Select(picture => new ReferencePlanes(format.CropWidth, format.CropHeight, format.BitDepthLuma, format.ChromaFormatIdc, picture[0], picture[1], picture[2], signal, decoders))];
    }

    private static ReferenceHevcSequence ReadSequenceParameterSet(ReferenceHeif.HevcConfiguration configuration, string name)
    {
        var units = configuration.NalUnits.Where(unit => ReferenceHevcStream.GetNalUnitType(unit.Span) is ReferenceHevcStream.NalSps).ToList();
        Assert.HasCount(1, units, $"{name}: the configuration record does not have one sequence parameter set.");
        return ReferenceHevcStream.ReadSequenceParameterSet(units[0].Span);
    }

    /// <summary>
    /// Decodes a byte stream with FFmpeg and dec265, and with HM when they disagree: the planes are those that two decoders
    /// give, byte for byte (HEVC decoding is normative; no reference decoder is right on every stream).
    /// </summary>
    /// <returns>The three planes of each picture (the chroma planes of a monochrome picture are empty), the video signal description of the stream, and the decoders that agree.</returns>
    private static async Task<(IReadOnlyList<ushort[][]> Pictures, VideoSignal Signal, string Decoders)> DecodeStreamAsync(string name, byte[] stream, ReferenceHevcSequence format, int pictureCount)
    {
        var path = ArtifactPath(name + ".hevc");
        await File.WriteAllBytesAsync(path, stream, XunitCancellationToken);
        var scratch = ArtifactPath(name + ".reference");
        var results = new List<(string Name, HevcReferenceDecoderResult Result)>();
        ReadOnlyMemory<byte>? agreed = null;
        var agreeing = "";
        foreach (var decoderName in new[] { HevcReferenceDecoder.FFmpeg, HevcReferenceDecoder.Dec265, HevcReferenceDecoder.Hm })
        {
            if (!ReferenceDecoders.TryGetValue(decoderName, out var decoder))
            {
                decoder = ReferenceDecoders.GetOrAdd(decoderName, await HevcReferenceDecoder.LocateAsync(decoderName, XunitCancellationToken));
            }

            var result = await decoder.DecodeAsync(path, scratch, XunitCancellationToken);
            foreach (var (otherName, other) in results)
            {
                if (agreed is null && result.Succeeded && other.Succeeded && result.Planes.Span.SequenceEqual(other.Planes.Span))
                {
                    agreed = result.Planes;
                    agreeing = $"{otherName}+{decoderName}";
                }
            }

            results.Add((decoderName, result));
            if (agreed is not null)
                break;
        }

        if (agreed is not { } planes)
        {
            Assert.Fail($"{name}: no two reference decoders agree on the planes of '{path}': {string.Join("; ", results.Select(result => $"{result.Name}: {result.Result.Error ?? string.Create(CultureInfo.InvariantCulture, $"{result.Result.Planes.Length} bytes")}"))}.");
            throw new InvalidOperationException("Unreachable.");
        }

        // One byte per sample of 8 bits, two bytes otherwise, least significant byte first; the planes one after the other
        var bytesPerSample = format.BitDepthLuma > 8 ? 2 : 1;
        var lumaLength = format.CropWidth * format.CropHeight;
        var chromaLength = format.ChromaFormatIdc is 0 ? 0 : (format.CropWidth / format.SubWidthC) * (format.CropHeight / format.SubHeightC);
        var pictureLength = (lumaLength + (2 * chromaLength)) * bytesPerSample;
        Assert.Equal(pictureCount * pictureLength, planes.Length, message: $"{name}: the reference decoders ({agreeing}) do not give {pictureCount} picture(s) of {format.CropWidth}x{format.CropHeight} samples.");
        var pictures = new List<ushort[][]>();
        for (var picture = 0; picture < pictureCount; picture++)
        {
            var bytes = planes.Span.Slice(picture * pictureLength, pictureLength);
            pictures.Add(
            [
                ReadSamples(bytes[..(lumaLength * bytesPerSample)], bytesPerSample),
                ReadSamples(bytes.Slice(lumaLength * bytesPerSample, chromaLength * bytesPerSample), bytesPerSample),
                ReadSamples(bytes[((lumaLength + chromaLength) * bytesPerSample)..], bytesPerSample),
            ]);
        }

        return (pictures, await ReadVideoSignalAsync(path, name), agreeing);

        static ushort[] ReadSamples(ReadOnlySpan<byte> bytes, int bytesPerSample)
        {
            var samples = new ushort[bytes.Length / bytesPerSample];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = bytesPerSample == 1 ? bytes[i] : BinaryPrimitives.ReadUInt16LittleEndian(bytes[(i * 2)..]);
            }

            return samples;
        }
    }

    /// <summary>Reads the video signal description of the sequence parameter sets of a byte stream, which must all carry the same one.</summary>
    private static async Task<VideoSignal> ReadVideoSignalAsync(FullPath streamPath, string name)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var trace = await ffmpeg.TraceHeadersAsync(streamPath, format: "hevc", XunitCancellationToken);
        Assert.NotNull(ReadElement(trace, "sps_seq_parameter_set_id", name), $"{name}: FFmpeg traces no sequence parameter set in '{streamPath}'.");
        return new VideoSignal(
            ReadElement(trace, "video_signal_type_present_flag", name) is 1,
            ReadElement(trace, "video_full_range_flag", name) is 1,
            ReadElement(trace, "colour_description_present_flag", name) is 1,
            ReadElement(trace, "colour_primaries", name) ?? 2,
            ReadElement(trace, "transfer_characteristics", name) ?? 2,
            ReadElement(trace, "matrix_coefficients", name) ?? 2,
            ReadElement(trace, "chroma_sample_loc_type_top_field", name) ?? 0,
            ReadElementValues(trace, "slice_type"));
    }

    /// <summary>
    /// Reads the value that a <c>trace_headers</c> log gives to a syntax element, which must be the same on every line that
    /// has the element; <see langword="null"/> when no line has it.
    /// </summary>
    private static int? ReadElement(string trace, string element, string name)
    {
        var values = ReadElementValues(trace, element).Distinct().ToList();
        if (values.Count > 1)
            Assert.Fail($"{name}: the parameter sets of the stream give the values {string.Join(", ", values)} to '{element}'.");

        return values.Count == 0 ? null : values[0];
    }

    /// <summary>Reads the values that a <c>trace_headers</c> log gives to a syntax element, one per line that has the element.</summary>
    private static List<int> ReadElementValues(string trace, string element)
    {
        var values = new List<int>();
        foreach (var line in trace.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            // "[trace_headers @ 0x...] 208         video_full_range_flag                                       1 = 1"
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 5 && parts[^2] == "=" && parts.Contains(element, StringComparer.Ordinal))
            {
                values.Add(int.Parse(parts[^1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));
            }
        }

        return values;
    }

    /// <summary>
    /// The matrix coefficients and the range that convert the samples of an image, by the rule the library documents: a
    /// <c>colr</c> box of type <c>nclx</c> wins over the bitstream; otherwise the video signal description of the bitstream
    /// applies, and what it does not signal is the limited range (the value that ITU-T H.265, clause E.3.1 infers) and
    /// unspecified matrix coefficients, which are converted as BT.601.
    /// </summary>
    internal static (int MatrixCoefficients, bool FullRange) ResolveColor(ReferenceHeif.ColorDescription? nclx, VideoSignal signal)
    {
        if (nclx is not null)
            return (nclx.MatrixCoefficients, nclx.FullRange);

        return (signal.HasColorDescription ? signal.MatrixCoefficients : 2, signal.HasVideoSignalType && signal.FullRange);
    }

    /// <summary>The color description the library reports: the <c>nclx</c> box, or else the color description of the bitstream when it has one.</summary>
    internal static CicpColorDescription? ExpectedCicp(ReferenceHeif.ColorDescription? nclx, VideoSignal signal)
    {
        if (nclx is not null)
            return new CicpColorDescription((byte)nclx.ColorPrimaries, (byte)nclx.TransferCharacteristics, (byte)nclx.MatrixCoefficients, nclx.FullRange);

        return signal.HasColorDescription
            ? new CicpColorDescription((byte)signal.ColorPrimaries, (byte)signal.TransferCharacteristics, (byte)signal.MatrixCoefficients, signal.HasVideoSignalType && signal.FullRange)
            : null;
    }

    /// <summary>
    /// The range of the samples of an alpha plane: the range that its own bitstream signals, and the full range that an
    /// alpha plane has (ISO/IEC 23000-22, section 7.3.5.2) when it signals none.
    /// </summary>
    internal static bool AlphaFullRange(VideoSignal signal) => !signal.HasVideoSignalType || signal.FullRange;

    /// <summary>
    /// Converts reference planes to straight RGBA pixels of 16 bits per sample with <see cref="ReferenceYuvConverter"/>, at the
    /// precision of the library: the pixels of 8-bit planes are computed on 8 bits (and multiplied by 257), and on 16 bits
    /// when the color planes or the alpha plane have deeper samples.
    /// </summary>
    /// <param name="color">The planes of the color image.</param>
    /// <param name="alpha">The planes of the alpha image, of the size of the color image; or <see langword="null"/>.</param>
    /// <param name="matrixCoefficients">The matrix coefficients of the color image.</param>
    /// <param name="fullRange">Whether the color samples use the full range.</param>
    /// <param name="crop">The rectangle of the planes that the clean aperture keeps; the whole planes without clean aperture.</param>
    /// <param name="model">The position of the chroma samples, and the order of the upsampling and of the crop.</param>
    /// <param name="reduceToEightBits">
    /// Whether the samples of the planes are reduced to their 8 most significant bits before anything else: what
    /// <c>heif-dec -S</c> does to the images of a sequence, whatever their bit depth.
    /// </param>
    internal static RawPixelBuffer ConvertToPixels(ReferencePlanes color, ReferencePlanes? alpha, int matrixCoefficients, bool fullRange, (int X, int Y, int Width, int Height) crop, ChromaModel model, bool reduceToEightBits = false)
    {
        if (reduceToEightBits)
        {
            color = Reduce(color);
            alpha = alpha is null ? null : Reduce(alpha);
        }

        // The precision of the pixels of the library: 8 bits when every plane has 8-bit samples
        var outputMaximum = Math.Max(color.BitDepth, alpha?.BitDepth ?? 8) == 8 ? 255 : 65535;
        var scale = outputMaximum == 255 ? 257 : 1;
        Assert.True(crop.X >= 0 && crop.Y >= 0 && crop.X + crop.Width <= color.Width && crop.Y + crop.Height <= color.Height, "The crop rectangle exceeds the planes.");
        var result = new byte[RawPixelLayout.Rgba16Le.GetByteLength(crop.Width, crop.Height)];
        int[]? rgb = null;
        var (rgbWidth, rgbX, rgbY) = (color.Width, crop.X, crop.Y);
        if (!color.IsMonochrome)
        {
            var (y, u, v) = (color.Y, color.U, color.V);
            var (width, height) = (color.Width, color.Height);
            var (cositedHorizontally, cositedVertically) = ReferenceYuvConverter.GetHevcChromaSiting(color.ChromaFormatIdc, color.Signal.ChromaSampleLocType);
            if (model is not ChromaModel.Declared)
            {
                Assert.Equal((0, 0), (crop.X & color.SubsamplingX, crop.Y & color.SubsamplingY), message: "The crop rectangle does not start on a chroma sample.");
                (width, height) = (crop.Width, crop.Height);
                y = CropPlane(color.Y, color.Width, crop.X, crop.Y, crop.Width, crop.Height);
                var chromaStride = (color.Width + color.SubsamplingX) >> color.SubsamplingX;
                var (chromaWidth, chromaHeight) = ((crop.Width + color.SubsamplingX) >> color.SubsamplingX, (crop.Height + color.SubsamplingY) >> color.SubsamplingY);
                u = CropPlane(color.U, chromaStride, crop.X >> color.SubsamplingX, crop.Y >> color.SubsamplingY, chromaWidth, chromaHeight);
                v = CropPlane(color.V, chromaStride, crop.X >> color.SubsamplingX, crop.Y >> color.SubsamplingY, chromaWidth, chromaHeight);
                (cositedHorizontally, cositedVertically) = (false, false);
                (rgbWidth, rgbX, rgbY) = (crop.Width, 0, 0);
            }

            var (subsamplingX, subsamplingY) = (color.SubsamplingX, color.SubsamplingY);
            if (model is ChromaModel.LibHeifSequence)
            {
                u = Replicate(u, width, height, subsamplingX, subsamplingY);
                v = Replicate(v, width, height, subsamplingX, subsamplingY);
                (subsamplingX, subsamplingY) = (0, 0);
            }

            rgb = ReferenceYuvConverter.ConvertToRgb(y, u, v, width, height, color.BitDepth, subsamplingX, subsamplingY, cositedHorizontally, cositedVertically, matrixCoefficients, fullRange, outputMaximum);
        }

        if (alpha is not null)
        {
            Assert.Equal((color.Width, color.Height, 0), (alpha.Width, alpha.Height, alpha.ChromaFormatIdc), message: "The alpha image is not a monochrome image of the size of the color image.");
        }

        for (var row = 0; row < crop.Height; row++)
        {
            for (var column = 0; column < crop.Width; column++)
            {
                var target = result.AsSpan(((row * crop.Width) + column) * 8, 8);
                var source = ((crop.Y + row) * color.Width) + crop.X + column;
                for (var channel = 0; channel < 3; channel++)
                {
                    // A monochrome image has no matrix: its samples are scaled from their range
                    var sample = rgb is null
                        ? ReferenceYuvConverter.ScaleSample(color.Y[source], color.BitDepth, fullRange, outputMaximum)
                        : rgb[(3 * (((rgbY + row) * rgbWidth) + rgbX + column)) + channel];
                    BinaryPrimitives.WriteUInt16LittleEndian(target[(channel * 2)..], (ushort)(sample * scale));
                }

                var opacity = alpha is null ? outputMaximum : ReferenceYuvConverter.ScaleSample(alpha.Y[source], alpha.BitDepth, AlphaFullRange(alpha.Signal), outputMaximum);
                BinaryPrimitives.WriteUInt16LittleEndian(target[6..], (ushort)(opacity * scale));
            }
        }

        return RawPixelBuffer.Create(crop.Width, crop.Height, RawPixelLayout.Rgba16Le, result);

        static ReferencePlanes Reduce(ReferencePlanes planes)
        {
            var shift = planes.BitDepth - 8;
            return planes with { BitDepth = 8, Y = [.. planes.Y.Select(sample => (ushort)(sample >> shift))], U = [.. planes.U.Select(sample => (ushort)(sample >> shift))], V = [.. planes.V.Select(sample => (ushort)(sample >> shift))] };
        }

        // Every luma sample takes the chroma sample of its group
        static ushort[] Replicate(ushort[] plane, int width, int height, int subsamplingX, int subsamplingY)
        {
            var stride = (width + subsamplingX) >> subsamplingX;
            var replicated = new ushort[width * height];
            for (var row = 0; row < height; row++)
            {
                for (var column = 0; column < width; column++)
                {
                    replicated[(row * width) + column] = plane[((row >> subsamplingY) * stride) + (column >> subsamplingX)];
                }
            }

            return replicated;
        }

        static ushort[] CropPlane(ushort[] plane, int stride, int x, int y, int width, int height)
        {
            var cropped = new ushort[width * height];
            for (var row = 0; row < height; row++)
            {
                plane.AsSpan(((y + row) * stride) + x, width).CopyTo(cropped.AsSpan(row * width));
            }

            return cropped;
        }
    }
}
