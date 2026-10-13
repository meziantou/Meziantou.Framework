using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes the boxes of a file of the HEIF family (ISO/IEC 23008-12 for the items and their properties; ISO/IEC 14496-12
/// for the boxes), for the format of a <see cref="HeifCodecBinding"/>: the binding gives the brands and the types of the
/// codec, and a <see cref="HeifStreamDescription"/> the configuration of each coded stream. It is the writer of the
/// encoders, independent of the builder that the tests use to produce the inputs of the decoder.
/// </summary>
internal static class HeifFileWriter
{
    private const int ColorItemId = 1;
    private const int AlphaItemId = 2;

    /// <summary>What a still image file describes besides its coded payloads.</summary>
    internal sealed class StillImage
    {
        public required int Width { get; init; }

        public required int Height { get; init; }

        /// <summary>Gets the description of the coded color image.</summary>
        public required HeifStreamDescription Color { get; init; }

        /// <summary>Gets the number of bytes of the coded color image.</summary>
        public required int ColorLength { get; init; }

        /// <summary>Gets the description of the coded alpha plane, or <see langword="null"/> for an opaque image.</summary>
        public HeifStreamDescription? Alpha { get; init; }

        public int AlphaLength { get; init; }

        /// <summary>Gets the color description of the <c>colr</c> box of type <c>nclx</c>.</summary>
        public required CicpColorDescription ColorDescription { get; init; }

        public ReadOnlyMemory<byte> IccProfile { get; init; }

        public ReadOnlyMemory<byte> Exif { get; init; }

        public ReadOnlyMemory<byte> Xmp { get; init; }

        public ExifOrientation Orientation { get; init; } = ExifOrientation.TopLeft;
    }

    /// <summary>
    /// Writes everything that precedes the coded images of a file: the <c>ftyp</c> and <c>meta</c> boxes, the header of the
    /// <c>mdat</c> box and the EXIF and XMP items. The file continues with the coded alpha plane, when there is one, then
    /// the coded color image.
    /// </summary>
    /// <remarks>
    /// An image sequence has the same boxes, which describe its first image as a still image for the readers that do not
    /// play sequences, and room for its <c>moov</c> box before the media data: a <c>free</c> box of
    /// <paramref name="movieReservation"/> bytes, which <see cref="WriteMovie"/> replaces once every image is written.
    /// Its <c>mdat</c> box has a 64-bit size, which is written at the same time.
    /// </remarks>
    /// <param name="output">The buffer that receives the boxes.</param>
    /// <param name="binding">The format of the file.</param>
    /// <param name="image">The image: the first one of a sequence.</param>
    /// <param name="movieReservation">The number of bytes to reserve for the <c>moov</c> box of an image sequence, or -1 for a still image.</param>
    /// <returns>For an image sequence, the position of the reserved bytes and of the size of the <c>mdat</c> box.</returns>
    /// <exception cref="UnsupportedImageFeatureException">The file would exceed 4 GiB.</exception>
    public static (int MovieOffset, int MediaSizeOffset) WritePrefix(PooledPayloadWriter output, HeifCodecBinding binding, StillImage image, int movieReservation = -1)
    {
        var isSequence = movieReservation >= 0;
        var writer = new HeifBoxWriter(output);
        var hasAlpha = image.Alpha is not null;
        var hasExif = !image.Exif.IsEmpty;
        var hasXmp = !image.Xmp.IsEmpty;
        var exifItemId = hasAlpha ? AlphaItemId + 1 : ColorItemId + 1;
        var xmpItemId = exifItemId + (hasExif ? 1 : 0);

        // ISO/IEC 14496-12, section 4.3 "File Type Box"
        var box = writer.BeginBox(HeifFormat.Ftyp);
        binding.WriteFileTypeBrands(writer, isSequence, image.Color, image.Alpha);
        writer.EndBox(box);

        var meta = writer.BeginFullBox(HeifFormat.Meta, version: 0, flags: 0);
        box = writer.BeginFullBox(HeifFormat.Hdlr, version: 0, flags: 0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(HeifFormat.Pict);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteCString(default);
        writer.EndBox(box);

        box = writer.BeginFullBox(HeifFormat.Pitm, version: 0, flags: 0);
        writer.WriteUInt16(ColorItemId);
        writer.EndBox(box);

        // Item location: one extent per item, with 4-byte offsets from the start of the file and 4-byte lengths. The
        // offsets are known once the meta box ends
        Span<int> extentOffsets = stackalloc int[4];
        Span<int> lengths = [image.ColorLength, image.AlphaLength, image.Exif.Length + (hasExif ? 4 : 0), image.Xmp.Length];
        Span<int> itemIds = [ColorItemId, AlphaItemId, exifItemId, xmpItemId];
        Span<bool> present = [true, hasAlpha, hasExif, hasXmp];
        var itemCount = 1 + (hasAlpha ? 1 : 0) + (hasExif ? 1 : 0) + (hasXmp ? 1 : 0);
        box = writer.BeginFullBox(HeifFormat.Iloc, version: 0, flags: 0);
        writer.WriteByte(0x44); // offset_size and length_size
        writer.WriteByte(0x00); // base_offset_size and reserved
        writer.WriteUInt16(itemCount);
        for (var i = 0; i < 4; i++)
        {
            if (!present[i])
                continue;

            writer.WriteUInt16(itemIds[i]);
            writer.WriteUInt16(0); // data_reference_index: this file
            writer.WriteUInt16(1); // extent_count
            extentOffsets[i] = writer.Length;
            writer.WriteUInt32(0);
            writer.WriteUInt32((uint)lengths[i]);
        }

        writer.EndBox(box);

        box = writer.BeginFullBox(HeifFormat.Iinf, version: 0, flags: 0);
        writer.WriteUInt16(itemCount);
        WriteItemInfo(writer, ColorItemId, binding.CodedItemType, contentType: default);
        if (hasAlpha)
            WriteItemInfo(writer, AlphaItemId, binding.CodedItemType, contentType: default);

        if (hasExif)
            WriteItemInfo(writer, exifItemId, HeifFormat.Exif, contentType: default);

        if (hasXmp)
            WriteItemInfo(writer, xmpItemId, HeifFormat.Mime, HeifFormat.XmpContentType);

        writer.EndBox(box);

        if (hasAlpha || hasExif || hasXmp)
        {
            box = writer.BeginFullBox(HeifFormat.Iref, version: 0, flags: 0);
            if (hasAlpha)
                WriteItemReference(writer, HeifFormat.Auxl, AlphaItemId, ColorItemId);

            if (hasExif)
                WriteItemReference(writer, HeifFormat.Cdsc, exifItemId, ColorItemId);

            if (hasXmp)
                WriteItemReference(writer, HeifFormat.Cdsc, xmpItemId, ColorItemId);

            writer.EndBox(box);
        }

        WriteItemProperties(writer, binding, image);
        writer.EndBox(meta);

        // The media data: the metadata items, the alpha plane, the color image
        long dataLength = 0;
        for (var i = 0; i < 4; i++)
        {
            if (present[i])
                dataLength += lengths[i];
        }

        var movieOffset = 0;
        var mediaSizeOffset = 0;
        if (isSequence)
        {
            // The room of the moov box: a free box until the box is written
            movieOffset = writer.Length;
            writer.WriteUInt32((uint)movieReservation);
            writer.WriteUInt32(HeifFormat.Free);
            output.WriteZeros(movieReservation - 8);
        }

        var mediaStart = writer.Length + (isSequence ? 16L : 8L);
        if (mediaStart + dataLength > uint.MaxValue)
            throw binding.Unsupported($"The {binding.Name} file would exceed 4 GiB, which this encoder does not write.", $"{binding.Name} file size");

        if (isSequence)
        {
            // A size of 1 announces the 64-bit size that follows the type
            writer.WriteUInt32(1);
            writer.WriteUInt32(HeifFormat.Mdat);
            mediaSizeOffset = writer.Length;
            writer.WriteUInt32(0);
            writer.WriteUInt32(0);
        }
        else
        {
            writer.WriteUInt32((uint)(8 + dataLength));
            writer.WriteUInt32(HeifFormat.Mdat);
        }

        var offset = mediaStart;
        Span<int> order = [2, 3, 1, 0];
        foreach (var i in order)
        {
            if (!present[i])
                continue;

            writer.PatchUInt32(extentOffsets[i], (uint)offset);
            offset += lengths[i];
        }

        if (hasExif)
        {
            // The offset of the TIFF header in the item: the data start with it
            writer.WriteUInt32(0);
            writer.WriteBytes(image.Exif.Span);
        }

        if (hasXmp)
            writer.WriteBytes(image.Xmp.Span);

        return (movieOffset, mediaSizeOffset);
    }

    /// <summary>What the <c>moov</c> box of an image sequence describes besides its samples.</summary>
    internal sealed class Movie
    {
        public required int Width { get; init; }

        public required int Height { get; init; }

        /// <summary>Gets the description of the coded color images.</summary>
        public required HeifStreamDescription Color { get; init; }

        /// <summary>Gets the description of the coded alpha planes, or <see langword="null"/> for an opaque sequence.</summary>
        public HeifStreamDescription? Alpha { get; init; }

        public required CicpColorDescription ColorDescription { get; init; }

        public ReadOnlyMemory<byte> IccProfile { get; init; }

        /// <summary>Gets the number of units of time of a second, for the durations of the images.</summary>
        public required uint Timescale { get; init; }

        /// <summary>Gets the number of times the sequence is played, or <see langword="null"/> when it repeats forever.</summary>
        public int? TotalPlays { get; init; }
    }

    /// <summary>
    /// Writes the <c>moov</c> box of an image sequence (ISO/IEC 14496-12, section 8.2), then the <c>free</c> box that fills
    /// what is left of <paramref name="reservation"/> bytes: a track of the color images and, for a sequence with alpha,
    /// an auxiliary track of the alpha planes. Each image is a sample and a chunk of each track; the samples of an image
    /// follow one another in the media data, the alpha plane first.
    /// </summary>
    /// <param name="output">The buffer that receives the boxes.</param>
    /// <param name="binding">The format of the file.</param>
    /// <param name="movie">The description of the sequence.</param>
    /// <param name="colorSizes">The size of each coded color image.</param>
    /// <param name="alphaSizes">The size of each coded alpha plane; empty without alpha.</param>
    /// <param name="durations">The duration of each image, in units of the time scale.</param>
    /// <param name="keyFrames">For each image, 1 when it is a key frame.</param>
    /// <param name="firstSampleOffset">The position of the first sample in the file.</param>
    /// <param name="reservation">The number of bytes to write, or 0 to measure: the result of a call with <paramref name="worstCase"/>.</param>
    /// <param name="worstCase">Whether the tables take the room of the least compressible sequence of this number of images.</param>
    /// <exception cref="UnsupportedImageFeatureException">The play count cannot be represented.</exception>
    public static void WriteMovie(PooledPayloadWriter output, HeifCodecBinding binding, Movie movie, ReadOnlySpan<int> colorSizes, ReadOnlySpan<int> alphaSizes, ReadOnlySpan<uint> durations, ReadOnlySpan<byte> keyFrames, long firstSampleOffset, int reservation, bool worstCase)
    {
        var writer = new HeifBoxWriter(output);
        var start = writer.Length;
        ulong mediaDuration = 0;
        foreach (var duration in durations)
        {
            mediaDuration += duration;
        }

        // AV1 Image File Format specification, section 3.2: the edit list of the track gives the duration of one play, and
        // the duration of the track the duration of all of them, or the largest value for a sequence that repeats forever
        var repeats = movie.TotalPlays is not 1;
        ulong trackDuration;
        if (movie.TotalPlays is { } plays)
        {
            if (plays > 1 && mediaDuration is 0)
                throw binding.Unsupported($"An {binding.Name} image sequence stores its play count as the ratio of two durations, so a sequence of images without duration cannot be played a finite number of times. Give the images a duration, or play the sequence once or forever.", $"{binding.Name} play count");

            // The duration of all the plays is a 64-bit field, whose largest value stands for a sequence that repeats forever
            var totalDuration = (UInt128)mediaDuration * (ulong)plays;
            if (totalDuration >= ulong.MaxValue)
                throw binding.Unsupported($"An {binding.Name} image sequence stores its play count as the duration of all its plays, which exceeds 64 bits for {plays} plays of these durations. Reduce the play count or the durations, or play the sequence forever.", $"{binding.Name} play count");

            trackDuration = (ulong)totalDuration;
        }
        else
        {
            trackDuration = ulong.MaxValue;
        }

        var moov = writer.BeginBox(HeifFormat.Moov);
        var box = writer.BeginFullBox(HeifFormat.Mvhd, version: 1, flags: 0);
        writer.WriteUInt64(0); // creation_time
        writer.WriteUInt64(0); // modification_time
        writer.WriteUInt32(movie.Timescale);
        writer.WriteUInt64(trackDuration);
        writer.WriteUInt32(0x00010000); // rate
        writer.WriteUInt16(0x0100); // volume
        writer.WriteUInt16(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        WriteUnityMatrix(writer);
        for (var i = 0; i < 6; i++)
        {
            writer.WriteUInt32(0); // pre_defined
        }

        writer.WriteUInt32(movie.Alpha is null ? 2u : 3u); // next_track_ID
        writer.EndBox(box);

        // The samples of an image: its alpha plane, then its color image
        WriteTrack(writer, binding, movie, isAlpha: false, colorSizes, alphaSizes, durations, keyFrames, firstSampleOffset, mediaDuration, trackDuration, repeats, worstCase);
        if (movie.Alpha is not null)
            WriteTrack(writer, binding, movie, isAlpha: true, alphaSizes, colorSizes, durations, keyFrames, firstSampleOffset, mediaDuration, trackDuration, repeats, worstCase);

        writer.EndBox(moov);
        if (reservation is 0)
            return;

        // What is left of the reserved bytes is a free box, whose content was written with the reservation
        var remaining = reservation - (writer.Length - start);
        if (remaining < 8)
            throw new InvalidOperationException("The moov box exceeds the bytes reserved for it.");

        writer.WriteUInt32((uint)remaining);
        writer.WriteUInt32(HeifFormat.Free);
    }

    // ISO/IEC 14496-12, section 12.1.4 "Clean Aperture Box": the image is the top-left part of pictures that are larger
    // than it. The center of the aperture is half the difference of the sizes before the center of the picture
    private static void WriteCleanAperture(HeifBoxWriter writer, int width, int height, Size codedSize)
    {
        var box = writer.BeginBox(HeifFormat.Clap);
        writer.WriteUInt32((uint)width);
        writer.WriteUInt32(1);
        writer.WriteUInt32((uint)height);
        writer.WriteUInt32(1);
        writer.WriteUInt32(unchecked((uint)(width - codedSize.Width)));
        writer.WriteUInt32(2);
        writer.WriteUInt32(unchecked((uint)(height - codedSize.Height)));
        writer.WriteUInt32(2);
        writer.EndBox(box);
    }

    private static void WriteTrack(HeifBoxWriter writer, HeifCodecBinding binding, Movie movie, bool isAlpha, ReadOnlySpan<int> sizes, ReadOnlySpan<int> otherSizes, ReadOnlySpan<uint> durations, ReadOnlySpan<byte> keyFrames, long firstSampleOffset, ulong mediaDuration, ulong trackDuration, bool repeats, bool worstCase)
    {
        var stream = isAlpha ? movie.Alpha! : movie.Color;
        var trackId = isAlpha ? 2u : 1u;
        var count = sizes.Length;
        var trak = writer.BeginBox(HeifFormat.Trak);

        // ISO/IEC 14496-12, section 8.3.2 "Track Header Box": enabled, in the movie
        var box = writer.BeginFullBox(HeifFormat.Tkhd, version: 1, flags: 3);
        writer.WriteUInt64(0);
        writer.WriteUInt64(0);
        writer.WriteUInt32(trackId);
        writer.WriteUInt32(0);
        writer.WriteUInt64(trackDuration);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteUInt16(0); // layer
        writer.WriteUInt16(0); // alternate_group
        writer.WriteUInt16(0); // volume
        writer.WriteUInt16(0);
        WriteUnityMatrix(writer);
        writer.WriteUInt32((uint)movie.Width << 16);
        writer.WriteUInt32((uint)movie.Height << 16);
        writer.EndBox(box);

        if (isAlpha)
        {
            // The alpha track is an auxiliary track of the color track
            box = writer.BeginBox(HeifFormat.Tref);
            var reference = writer.BeginBox(HeifFormat.Auxl);
            writer.WriteUInt32(1);
            writer.EndBox(reference);
            writer.EndBox(box);
        }

        // ISO/IEC 14496-12, section 8.6.6 "Edit List Box": one edit that plays the whole media, repeated or not. An edit
        // without duration cannot be repeated (libavif rejects the file): a sequence of images without duration that
        // repeats forever has no edit list, which readers play forever or an unknown number of times
        if (worstCase || !repeats || mediaDuration is not 0)
        {
            box = writer.BeginBox(HeifFormat.Edts);
            var elst = writer.BeginFullBox(HeifFormat.Elst, version: 1, flags: repeats ? 1 : 0);
            writer.WriteUInt32(1);
            writer.WriteUInt64(mediaDuration);
            writer.WriteUInt64(0); // media_time
            writer.WriteUInt32(0x00010000); // media_rate
            writer.EndBox(elst);
            writer.EndBox(box);
        }

        var mdia = writer.BeginBox(HeifFormat.Mdia);
        box = writer.BeginFullBox(HeifFormat.Mdhd, version: 1, flags: 0);
        writer.WriteUInt64(0);
        writer.WriteUInt64(0);
        writer.WriteUInt32(movie.Timescale);
        writer.WriteUInt64(mediaDuration);
        writer.WriteUInt16(0x55C4); // language: undetermined
        writer.WriteUInt16(0);
        writer.EndBox(box);

        box = writer.BeginFullBox(HeifFormat.Hdlr, version: 0, flags: 0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(isAlpha ? HeifFormat.Auxv : HeifFormat.Pict);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteCString(default);
        writer.EndBox(box);

        var minf = writer.BeginBox(HeifFormat.Minf);
        box = writer.BeginFullBox(HeifFormat.Vmhd, version: 0, flags: 1);
        writer.WriteUInt32(0); // graphicsmode and the first component of opcolor
        writer.WriteUInt32(0);
        writer.EndBox(box);

        // The media data are in this file
        box = writer.BeginBox(HeifFormat.Dinf);
        var dref = writer.BeginFullBox(HeifFormat.Dref, version: 0, flags: 0);
        writer.WriteUInt32(1);
        var url = writer.BeginFullBox(HeifFormat.Url, version: 0, flags: 1);
        writer.EndBox(url);
        writer.EndBox(dref);
        writer.EndBox(box);

        var stbl = writer.BeginBox(HeifFormat.Stbl);
        box = writer.BeginFullBox(HeifFormat.Stsd, version: 0, flags: 0);
        writer.WriteUInt32(1);

        // ISO/IEC 14496-12, section 12.1.3 "Sample entry" of a visual track
        // The pictures of the stream may be larger than the images, which are then their clean aperture
        var codedSize = movie.Color.OutputSize ?? new Size(movie.Width, movie.Height);
        var entry = writer.BeginBox(binding.SampleEntryType);
        writer.WriteUInt32(0);
        writer.WriteUInt16(0);
        writer.WriteUInt16(1); // data_reference_index
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteUInt16((ushort)codedSize.Width);
        writer.WriteUInt16((ushort)codedSize.Height);
        writer.WriteUInt32(0x00480000); // horizresolution: 72 dpi
        writer.WriteUInt32(0x00480000); // vertresolution
        writer.WriteUInt32(0);
        writer.WriteUInt16(1); // frame_count
        for (var i = 0; i < 8; i++)
        {
            writer.WriteUInt32(0); // compressorname
        }

        writer.WriteUInt16(0x0018); // depth
        writer.WriteUInt16(0xFFFF); // pre_defined
        stream.WriteConfigurationBox(writer);
        if (codedSize.Width != movie.Width || codedSize.Height != movie.Height)
            WriteCleanAperture(writer, movie.Width, movie.Height, codedSize);

        if (isAlpha)
        {
            // ISO/IEC 23008-12, section 7.5.3 "Auxiliary video track"
            var auxi = writer.BeginFullBox(HeifFormat.Auxi, version: 0, flags: 0);
            writer.WriteCString(binding.AlphaAuxiliaryTypeToWrite);
            writer.EndBox(auxi);
        }
        else
        {
            WriteColorInformation(writer, movie.ColorDescription, movie.IccProfile);
        }

        // ISO/IEC 23008-12, section 7.2.3 "Coding constraints box": the images may predict from one another, and from the
        // samples of their own image
        var ccst = writer.BeginFullBox(HeifFormat.Ccst, version: 0, flags: 0);
        writer.WriteUInt32(0x7C000000); // all_ref_pics_intra 0, intra_pred_used 1, max_ref_per_pic 15
        writer.EndBox(ccst);
        writer.EndBox(entry);
        writer.EndBox(box);

        // The durations, with their repetitions counted
        box = writer.BeginFullBox(HeifFormat.Stts, version: 0, flags: 0);
        var entryCountOffset = writer.Length;
        writer.WriteUInt32(0);
        var entries = 0u;
        for (var i = 0; i < count;)
        {
            var run = 1;
            while (!worstCase && i + run < count && durations[i + run] == durations[i])
            {
                run++;
            }

            writer.WriteUInt32((uint)run);
            writer.WriteUInt32(durations[i]);
            entries++;
            i += run;
        }

        writer.PatchUInt32(entryCountOffset, entries);
        writer.EndBox(box);

        // One sample per chunk
        box = writer.BeginFullBox(HeifFormat.Stsc, version: 0, flags: 0);
        writer.WriteUInt32(1);
        writer.WriteUInt32(1);
        writer.WriteUInt32(1);
        writer.WriteUInt32(1);
        writer.EndBox(box);

        box = writer.BeginFullBox(HeifFormat.Stsz, version: 0, flags: 0);
        writer.WriteUInt32(0);
        writer.WriteUInt32((uint)count);
        foreach (var size in sizes)
        {
            writer.WriteUInt32((uint)size);
        }

        writer.EndBox(box);

        // The offsets of the samples: 32-bit ones (stco) when the file allows them, since some readers have no support for
        // the 64-bit ones (co64), which are the worst case
        long mediaLength = 0;
        for (var i = 0; i < count; i++)
        {
            mediaLength += sizes[i] + (otherSizes.IsEmpty ? 0 : otherSizes[i]);
        }

        var wideOffsets = worstCase || firstSampleOffset + mediaLength > uint.MaxValue;
        box = writer.BeginFullBox(wideOffsets ? HeifFormat.Co64 : HeifFormat.Stco, version: 0, flags: 0);
        writer.WriteUInt32((uint)count);
        var offset = firstSampleOffset;
        for (var i = 0; i < count; i++)
        {
            // The alpha plane of an image precedes its color image
            var alphaSize = isAlpha ? sizes[i] : (otherSizes.IsEmpty ? 0 : otherSizes[i]);
            var colorSize = isAlpha ? otherSizes[i] : sizes[i];
            var sampleOffset = isAlpha ? offset : offset + alphaSize;
            if (wideOffsets)
            {
                writer.WriteUInt64((ulong)sampleOffset);
            }
            else
            {
                writer.WriteUInt32((uint)sampleOffset);
            }

            offset += alphaSize + colorSize;
        }

        writer.EndBox(box);

        // The sync samples, unless every sample is one
        var syncCount = 0;
        foreach (var keyFrame in keyFrames)
        {
            syncCount += keyFrame;
        }

        if (worstCase || syncCount != count)
        {
            box = writer.BeginFullBox(HeifFormat.Stss, version: 0, flags: 0);
            writer.WriteUInt32((uint)(worstCase ? count : syncCount));
            for (var i = 0; i < count; i++)
            {
                if (worstCase || keyFrames[i] is not 0)
                    writer.WriteUInt32((uint)(i + 1));
            }

            writer.EndBox(box);
        }

        writer.EndBox(stbl);
        writer.EndBox(minf);
        writer.EndBox(mdia);
        writer.EndBox(trak);
    }

    private static void WriteUnityMatrix(HeifBoxWriter writer)
    {
        ReadOnlySpan<uint> matrix = [0x00010000, 0, 0, 0, 0x00010000, 0, 0, 0, 0x40000000];
        foreach (var value in matrix)
        {
            writer.WriteUInt32(value);
        }
    }

    // ISO/IEC 14496-12, section 12.1.5 "Colour information": the code points, then the profile that takes precedence.
    // Returns the number of boxes
    private static int WriteColorInformation(HeifBoxWriter writer, CicpColorDescription description, ReadOnlyMemory<byte> iccProfile)
    {
        var box = writer.BeginBox(HeifFormat.Colr);
        writer.WriteUInt32(HeifFormat.Nclx);
        writer.WriteUInt16(description.ColorPrimaries);
        writer.WriteUInt16(description.TransferCharacteristics);
        writer.WriteUInt16(description.MatrixCoefficients);
        writer.WriteByte((byte)(description.FullRange ? 0x80 : 0));
        writer.EndBox(box);
        if (iccProfile.IsEmpty)
            return 1;

        box = writer.BeginBox(HeifFormat.Colr);
        writer.WriteUInt32(HeifFormat.Prof);
        writer.WriteBytes(iccProfile.Span);
        writer.EndBox(box);
        return 2;
    }

    /// <summary>
    /// Returns the rotation (<c>irot</c> angle, in anti-clockwise quarter turns) and the mirroring (<c>imir</c> axis, or -1)
    /// that, applied in this order, display the image upright: what the decoder composes into <paramref name="orientation"/>.
    /// </summary>
    public static (int Angle, int Axis) GetTransformations(ExifOrientation orientation)
    {
        // The fewest properties first: a rotation alone, a mirroring alone, then both
        ReadOnlySpan<(int Angle, int Axis)> candidates =
        [
            (0, -1), (1, -1), (2, -1), (3, -1),
            (0, 0), (0, 1),
            (1, 0), (2, 0), (3, 0),
            (1, 1), (2, 1), (3, 1),
        ];

        foreach (var (angle, axis) in candidates)
        {
            var transform = angle switch
            {
                1 => OrientationTransform.ForRotation(RotateMode.Rotate270),
                2 => OrientationTransform.ForRotation(RotateMode.Rotate180),
                3 => OrientationTransform.ForRotation(RotateMode.Rotate90),
                _ => OrientationTransform.Identity,
            };

            if (axis >= 0)
                transform = transform.Then(OrientationTransform.ForFlip(axis is 0 ? FlipMode.Vertical : FlipMode.Horizontal));

            if (transform.ToExifOrientation() == orientation)
                return (angle, axis);
        }

        throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "The orientation must be between 1 and 8.");
    }

    // ISO/IEC 14496-12, section 8.11.6 "Item Information Box": an entry of version 2
    private static void WriteItemInfo(HeifBoxWriter writer, int itemId, uint itemType, ReadOnlySpan<byte> contentType)
    {
        var box = writer.BeginFullBox(HeifFormat.Infe, version: 2, flags: 0);
        writer.WriteUInt16(itemId);
        writer.WriteUInt16(0); // item_protection_index
        writer.WriteUInt32(itemType);
        writer.WriteCString(default); // item_name
        if (itemType is HeifFormat.Mime)
            writer.WriteCString(contentType);

        writer.EndBox(box);
    }

    // ISO/IEC 14496-12, section 8.11.12 "Item Reference Box"
    private static void WriteItemReference(HeifBoxWriter writer, uint type, int fromItemId, int toItemId)
    {
        var box = writer.BeginBox(type);
        writer.WriteUInt16(fromItemId);
        writer.WriteUInt16(1);
        writer.WriteUInt16(toItemId);
        writer.EndBox(box);
    }

    // ISO/IEC 23008-12, section 9.3 "Item Properties Box": the properties, then their association with the items. The
    // properties that a reader must understand to display the item are marked essential
    private static void WriteItemProperties(HeifBoxWriter writer, HeifCodecBinding binding, StillImage image)
    {
        const int Essential = 0x80;
        Span<byte> color = stackalloc byte[9];
        Span<byte> alpha = stackalloc byte[9];
        var colorCount = 0;
        var alphaCount = 0;
        var index = 0;
        var iprp = writer.BeginBox(HeifFormat.Iprp);
        var ipco = writer.BeginBox(HeifFormat.Ipco);

        // The size of the coded pictures, shared by the color image and the alpha plane
        var codedSize = image.Color.OutputSize ?? new Size(image.Width, image.Height);
        var box = writer.BeginFullBox(HeifFormat.Ispe, version: 0, flags: 0);
        writer.WriteUInt32((uint)codedSize.Width);
        writer.WriteUInt32((uint)codedSize.Height);
        writer.EndBox(box);
        index++;
        color[colorCount++] = (byte)index;
        alpha[alphaCount++] = (byte)index;

        WritePixelInformation(writer, image.Color);
        color[colorCount++] = (byte)++index;
        image.Color.WriteConfigurationBox(writer);
        color[colorCount++] = (byte)(++index | Essential);

        for (var i = WriteColorInformation(writer, image.ColorDescription, image.IccProfile); i > 0; i--)
        {
            color[colorCount++] = (byte)++index;
        }

        // The image is the top-left part of pictures that are larger than it
        var apertureIndex = 0;
        if (codedSize.Width != image.Width || codedSize.Height != image.Height)
        {
            WriteCleanAperture(writer, image.Width, image.Height, codedSize);
            apertureIndex = ++index;
            color[colorCount++] = (byte)(index | Essential);
        }

        // The transformations apply to the alpha plane as they do to the color image
        var (angle, axis) = GetTransformations(image.Orientation);
        if (angle is not 0)
        {
            box = writer.BeginBox(HeifFormat.Irot);
            writer.WriteByte((byte)angle);
            writer.EndBox(box);
            index++;
            color[colorCount++] = (byte)(index | Essential);
        }

        var rotationIndex = angle is not 0 ? index : 0;
        if (axis >= 0)
        {
            box = writer.BeginBox(HeifFormat.Imir);
            writer.WriteByte((byte)axis);
            writer.EndBox(box);
            index++;
            color[colorCount++] = (byte)(index | Essential);
        }

        var mirrorIndex = axis >= 0 ? index : 0;
        if (image.Alpha is { } alphaStream)
        {
            WritePixelInformation(writer, alphaStream);
            alpha[alphaCount++] = (byte)++index;
            alphaStream.WriteConfigurationBox(writer);
            alpha[alphaCount++] = (byte)(++index | Essential);

            // ISO/IEC 23008-12, section 6.5.8 "Auxiliary type property"
            box = writer.BeginFullBox(HeifFormat.AuxC, version: 0, flags: 0);
            writer.WriteCString(binding.AlphaAuxiliaryTypeToWrite);
            writer.EndBox(box);
            alpha[alphaCount++] = (byte)++index;
            if (apertureIndex is not 0)
                alpha[alphaCount++] = (byte)(apertureIndex | Essential);

            if (rotationIndex is not 0)
                alpha[alphaCount++] = (byte)(rotationIndex | Essential);

            if (mirrorIndex is not 0)
                alpha[alphaCount++] = (byte)(mirrorIndex | Essential);
        }

        writer.EndBox(ipco);

        box = writer.BeginFullBox(HeifFormat.Ipma, version: 0, flags: 0);
        writer.WriteUInt32(image.Alpha is null ? 1u : 2u);
        writer.WriteUInt16(ColorItemId);
        writer.WriteByte((byte)colorCount);
        writer.WriteBytes(color[..colorCount]);
        if (image.Alpha is not null)
        {
            writer.WriteUInt16(AlphaItemId);
            writer.WriteByte((byte)alphaCount);
            writer.WriteBytes(alpha[..alphaCount]);
        }

        writer.EndBox(box);
        writer.EndBox(iprp);
    }

    // ISO/IEC 23008-12, section 6.5.6 "Pixel information"
    private static void WritePixelInformation(HeifBoxWriter writer, HeifStreamDescription stream)
    {
        var box = writer.BeginFullBox(HeifFormat.Pixi, version: 0, flags: 0);
        writer.WriteByte((byte)stream.PlaneCount);
        for (var i = 0; i < stream.PlaneCount; i++)
        {
            writer.WriteByte((byte)stream.BitDepth);
        }

        writer.EndBox(box);
    }
}
