namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The JPEG reconstruction data of a JPEG XL file (the <c>jbrd</c> box, ISO/IEC 18181-2, annex A): a bundle of bit fields
/// that describes the JPEG file the codestream was made from, then a Brotli stream of the segments the codestream does not
/// hold. The size of the image, the sampling factors, the quantization values, the coefficients, and the ICC profile, the
/// EXIF data and the XMP packet of the application segments that held them, are in the codestream and in the other boxes.
/// </summary>
internal sealed class JpegXlJpegReconstructionData
{
    /// <summary>An application segment that the box holds.</summary>
    public const int SegmentUnknown = 0;

    /// <summary>An application segment that holds a part of the ICC profile of the codestream.</summary>
    public const int SegmentIcc = 1;

    /// <summary>An application segment that holds the content of the <c>Exif</c> box.</summary>
    public const int SegmentExif = 2;

    /// <summary>An application segment that holds the content of the <c>xml </c> box.</summary>
    public const int SegmentXmp = 3;

    /// <summary>The components are one gray component, of identifier 1.</summary>
    public const int ComponentsGray = 0;

    /// <summary>The components are Y, Cb and Cr, of identifiers 1, 2 and 3.</summary>
    public const int ComponentsYCbCr = 1;

    /// <summary>The components are red, green and blue, of identifiers 'R', 'G' and 'B'.</summary>
    public const int ComponentsRgb = 2;

    /// <summary>The components have the identifiers the box lists.</summary>
    public const int ComponentsCustom = 3;

    private const int MaxMarkerCount = 16384;
    private const int MaxListLength = 1 << 24;

    private JpegXlJpegReconstructionData()
    {
    }

    /// <summary>Gets the description of the file. Its application segments of the kinds the codestream holds are empty until they are completed.</summary>
    public JpegBitstreamModel Model { get; } = new();

    /// <summary>Gets the kind of each application segment and the value of its length field.</summary>
    public List<(int Kind, int Length)> ApplicationSegments { get; } = [];

    /// <summary>Gets a value indicating whether the file has a single component, coded in the Y channel of the frame.</summary>
    public bool IsGray { get; private set; }

    /// <summary>Gets what the components are.</summary>
    public int ComponentKind { get; private set; }

    /// <summary>Reads the payload of a <c>jbrd</c> box.</summary>
    /// <param name="payload">The buffer that holds the payload.</param>
    /// <param name="length">The length of the payload.</param>
    /// <param name="tracker">The tracker the segments of the Brotli stream are charged to, as metadata.</param>
    /// <exception cref="InvalidImageContentException">The data are malformed.</exception>
    public static JpegXlJpegReconstructionData Read(byte[] payload, int length, InputResourceTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(tracker);
        var result = new JpegXlJpegReconstructionData();
        var model = result.Model;
        var reader = new JpegXlBitReader(payload, 0, length);
        result.IsGray = reader.ReadBool();

        // The markers, each as its distance to 0xC0, up to the end of image
        while (true)
        {
            var marker = (byte)(0xC0 + reader.ReadBits(6));
            model.MarkerOrder.Add(marker);
            if (marker == 0xD9)
                break;

            if (model.MarkerOrder.Count > MaxMarkerCount || reader.IsOverrun)
                throw Invalid("the list of the markers has no end.");
        }

        var applicationCount = model.MarkerOrder.Count(marker => marker is >= 0xE0 and <= 0xEF);
        var commentCount = model.MarkerOrder.Count(marker => marker == 0xFE);
        var scanCount = model.MarkerOrder.Count(marker => marker == 0xDA);
        var interMarkerCount = model.MarkerOrder.Count(marker => marker == JpegBitstreamModel.InterMarkerData);
        var hasRestartInterval = model.MarkerOrder.Contains(0xDD);
        if (model.MarkerOrder.Count(marker => marker is >= 0xC0 and <= 0xC2) != 1)
            throw Invalid("the file does not have exactly one frame header.");

        for (var i = 0; i < applicationCount; i++)
        {
            var kind = (int)reader.ReadU32(0, 0, 1, 0, 2, 1, 4, 2);
            var segmentLength = (int)reader.ReadBits(16);
            if (kind > SegmentXmp || segmentLength < 2)
                throw Invalid("an application segment is not valid.");

            result.ApplicationSegments.Add((kind, segmentLength));
        }

        var commentLengths = new int[commentCount];
        for (var i = 0; i < commentCount; i++)
        {
            commentLengths[i] = (int)reader.ReadBits(16);
            if (commentLengths[i] < 2)
                throw Invalid("a comment is not valid.");
        }

        var quantizationCount = (int)reader.ReadU32(1, 0, 2, 0, 3, 0, 4, 0);
        for (var i = 0; i < quantizationCount; i++)
        {
            var precision = (int)reader.ReadBits(1);
            var slot = (int)reader.ReadBits(2);
            model.QuantizationTables.Add(new JpegBitstreamQuantizationTable(precision, slot, reader.ReadBool()));
        }

        result.ComponentKind = (int)reader.ReadBits(2);
        int[] identifiers = result.ComponentKind switch
        {
            ComponentsGray => [1],
            ComponentsYCbCr => [1, 2, 3],
            ComponentsRgb => ['R', 'G', 'B'],
            _ => new int[(int)reader.ReadU32(1, 0, 2, 0, 3, 0, 4, 0)],
        };

        if (result.ComponentKind == ComponentsCustom)
        {
            for (var i = 0; i < identifiers.Length; i++)
            {
                identifiers[i] = (int)reader.ReadBits(8);
            }
        }

        if (identifiers.Length == 4 || result.IsGray != (identifiers.Length == 1))
            throw Invalid("the components are not those of a JPEG file that a JPEG XL frame holds.");

        foreach (var identifier in identifiers)
        {
            var table = (int)reader.ReadBits(2);
            if (table >= quantizationCount)
                throw Invalid("a component uses a quantization table that does not exist.");

            model.Components.Add(new JpegBitstreamComponent(identifier, table));
        }

        var huffmanCount = (int)reader.ReadU32(4, 0, 2, 3, 10, 4, 26, 6);
        Span<int> counts = stackalloc int[17];
        for (var i = 0; i < huffmanCount; i++)
        {
            var isAc = reader.ReadBool();
            var slot = (int)reader.ReadBits(2);
            var isLast = reader.ReadBool();
            var total = 0;
            for (var j = 0; j < counts.Length; j++)
            {
                counts[j] = (int)reader.ReadU32(0, 0, 1, 0, 2, 3, 0, 8);
                total += counts[j];
            }

            // The symbols, in the order of their codes, end with a symbol that no value has: it takes the code of all ones,
            // which a JPEG table does not use
            if (total is < 1 or > 257 || counts[0] != 0 || reader.IsOverrun)
                throw Invalid("a Huffman table is not valid.");

            var values = new byte[total - 1];
            for (var j = 0; j < total; j++)
            {
                var value = reader.ReadU32(0, 2, 4, 2, 8, 4, 1, 8);
                if (j == total - 1 ? value != 256 : value > 255)
                    throw Invalid("a Huffman table is not valid.");

                if (j < total - 1)
                {
                    values[j] = (byte)value;
                }
            }

            var last = 16;
            while (counts[last] == 0)
            {
                last--;
            }

            counts[last]--;
            var lengths = new byte[16];
            for (var j = 1; j <= 16; j++)
            {
                lengths[j - 1] = (byte)counts[j];
            }

            model.HuffmanTables.Add(new JpegBitstreamHuffmanTable(isAc, slot, isLast, lengths, values));
        }

        for (var i = 0; i < scanCount; i++)
        {
            var count = (int)reader.ReadU32(1, 0, 2, 0, 3, 0, 4, 0);
            var spectralStart = (int)reader.ReadBits(6);
            var spectralEnd = (int)reader.ReadBits(6);
            var successiveLow = (int)reader.ReadBits(4);
            var successiveHigh = (int)reader.ReadBits(4);
            var scan = new JpegBitstreamScan(spectralStart, spectralEnd, successiveHigh, successiveLow);
            for (var j = 0; j < count; j++)
            {
                var component = (int)reader.ReadBits(2);
                var acTable = (int)reader.ReadBits(2);
                var dcTable = (int)reader.ReadBits(2);
                if (component >= model.Components.Count)
                    throw Invalid("a scan codes a component that does not exist.");

                scan.Components.Add(new JpegBitstreamScanComponent(component, dcTable, acTable));
            }

            // The last pass a decoder needs to show the scan: not used to rebuild the file
            _ = reader.ReadU32(0, 0, 1, 0, 2, 0, 3, 3);
            model.Scans.Add(scan);
        }

        if (hasRestartInterval)
        {
            model.RestartInterval = (int)reader.ReadBits(16);
        }

        // For each scan, the blocks before which a run of blocks is written, then the blocks that end with runs of zeros:
        // each as its distance to the block that follows the previous one. The two lists of a scan follow each other
        // (established on files of several scans that have both: djxl does not rebuild a file that gives the first
        // list of every scan, then the second one)
        foreach (var scan in model.Scans)
        {
            var count = (int)reader.ReadU32(0, 0, 1, 2, 4, 4, 20, 16);
            long block = -1;
            for (var i = 0; i < count; i++)
            {
                block += 1 + reader.ReadU32(0, 0, 1, 3, 9, 5, 41, 28);
                if (block > int.MaxValue || reader.IsOverrun)
                    throw Invalid("the blocks of a scan are not valid.");

                scan.ResetPoints.Add((int)block);
            }

            count = (int)reader.ReadU32(0, 0, 1, 2, 4, 4, 20, 16);
            block = -1;
            for (var i = 0; i < count; i++)
            {
                var runs = (int)reader.ReadU32(1, 0, 2, 2, 5, 4, 20, 8);
                block += 1 + reader.ReadU32(0, 0, 1, 3, 9, 5, 41, 28);
                if (block > int.MaxValue || reader.IsOverrun)
                    throw Invalid("the blocks of a scan are not valid.");

                scan.ExtraZeroRuns.Add(((int)block, runs));
            }
        }

        var interMarkerLengths = new int[interMarkerCount];
        for (var i = 0; i < interMarkerCount; i++)
        {
            interMarkerLengths[i] = (int)reader.ReadBits(16);
        }

        var tailLength = (int)reader.ReadU32(0, 0, 1, 8, 257, 16, 65793, 22);
        if (reader.ReadBool())
        {
            var count = (int)reader.ReadBits(24);
            if (count > MaxListLength || count > reader.TotalBits - reader.BitsConsumed)
                throw Invalid("the padding bits are not valid.");

            tracker.ChargeMetadataBytes(count);
            var bits = new byte[count];
            for (var i = 0; i < count; i++)
            {
                bits[i] = (byte)reader.ReadBits(1);
            }

            model.PaddingBits = bits;
        }

        if (reader.IsOverrun)
            throw Invalid("the description of the file is truncated.");

        // The segments the codestream does not hold: the application segments, the comments, the data between markers and
        // the data after the end of image, one after the other in a Brotli stream
        reader.ZeroPadToByte();
        var consumed = (int)reader.BytesConsumed;
        long expected = tailLength;
        foreach (var (kind, segmentLength) in result.ApplicationSegments)
        {
            expected += kind == SegmentUnknown ? segmentLength + 1 : 0;
        }

        foreach (var commentLength in commentLengths)
        {
            expected += commentLength + 1;
        }

        foreach (var interMarkerLength in interMarkerLengths)
        {
            expected += interMarkerLength;
        }

        byte[] segments = [];
        if (consumed < length)
        {
            segments = BoundedBrotliDecoder.TryDecompress(payload.AsSpan(consumed, length - consumed), tracker) ?? throw Invalid("the segments of the file are not a Brotli stream.");
        }

        if (segments.Length != expected)
            throw Invalid("the segments of the file do not have the size the description gives.");

        var position = 0;
        foreach (var (kind, segmentLength) in result.ApplicationSegments)
        {
            if (kind != SegmentUnknown)
            {
                model.ApplicationSegments.Add([]);
                continue;
            }

            // The second byte of the marker, the length, the payload: the length must be the one of the description
            var segment = segments.AsSpan(position, segmentLength + 1);
            if (segment[0] is < 0xE0 or > 0xEF || ((segment[1] << 8) | segment[2]) != segmentLength)
                throw Invalid("an application segment does not have the marker or the length of its description.");

            model.ApplicationSegments.Add(segment.ToArray());
            position += segment.Length;
        }

        foreach (var commentLength in commentLengths)
        {
            var segment = segments.AsSpan(position, commentLength + 1);
            if (segment[0] != 0xFE || ((segment[1] << 8) | segment[2]) != commentLength)
                throw Invalid("a comment does not have the marker or the length of its description.");

            model.CommentSegments.Add(segment.ToArray());
            position += segment.Length;
        }

        foreach (var interMarkerLength in interMarkerLengths)
        {
            model.InterMarkerSegments.Add(segments.AsSpan(position, interMarkerLength).ToArray());
            position += interMarkerLength;
        }

        model.TailData = segments.AsSpan(position, tailLength).ToArray();
        return result;
    }

    /// <summary>Writes the payload of a <c>jbrd</c> box: the inverse of <see cref="Read"/>.</summary>
    /// <param name="model">The description of the JPEG file.</param>
    /// <param name="segmentKinds">The kind of each application segment: the content of the ones that are not <see cref="SegmentUnknown"/> is stored elsewhere in the JPEG XL file.</param>
    /// <param name="brotliQuality">The quality of the Brotli stream of the segments, from 0 to 11.</param>
    /// <param name="scope">The scope of the working memory.</param>
    /// <returns>The payload.</returns>
    /// <exception cref="UnsupportedImageFeatureException">The file has more of something than the box can describe.</exception>
    public static byte[] Write(JpegBitstreamModel model, ReadOnlySpan<int> segmentKinds, int brotliQuality, AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(scope);
        using var writer = new LsbBitWriter(scope);
        writer.WriteBool(model.Components.Count == 1);
        foreach (var marker in model.MarkerOrder)
        {
            if (marker < 0xC0)
                throw TooMuch("a marker below 0xC0");

            writer.WriteBits((uint)(marker - 0xC0), 6);
        }

        for (var i = 0; i < model.ApplicationSegments.Count; i++)
        {
            writer.WriteU32((uint)segmentKinds[i], 0, 0, 1, 0, 2, 1, 4, 2);
            writer.WriteBits((uint)(model.ApplicationSegments[i].Length - 1), 16);
        }

        foreach (var comment in model.CommentSegments)
        {
            writer.WriteBits((uint)(comment.Length - 1), 16);
        }

        writer.WriteU32((uint)model.QuantizationTables.Count, 1, 0, 2, 0, 3, 0, 4, 0);
        foreach (var table in model.QuantizationTables)
        {
            writer.WriteBits((uint)table.Precision, 1);
            writer.WriteBits((uint)table.Slot, 2);
            writer.WriteBool(table.IsLast);
        }

        // What the components are: the identifiers of JFIF, the ones of Adobe RGB files, or the ones the box lists
        var identifiers = model.Components.Select(component => component.Id).ToArray();
        var kind = identifiers switch
        {
            [1] => ComponentsGray,
            [1, 2, 3] => ComponentsYCbCr,
            ['R', 'G', 'B'] => ComponentsRgb,
            _ => ComponentsCustom,
        };
        writer.WriteBits((uint)kind, 2);
        if (kind == ComponentsCustom)
        {
            writer.WriteU32((uint)identifiers.Length, 1, 0, 2, 0, 3, 0, 4, 0);
            foreach (var identifier in identifiers)
            {
                writer.WriteBits((uint)identifier, 8);
            }
        }

        foreach (var component in model.Components)
        {
            writer.WriteBits((uint)component.QuantizationTable, 2);
        }

        if (model.HuffmanTables.Count is < 1 or > 26 + 63)
            throw TooMuch("that number of Huffman tables");

        writer.WriteU32((uint)model.HuffmanTables.Count, 4, 0, 2, 3, 10, 4, 26, 6);
        foreach (var table in model.HuffmanTables)
        {
            // The symbol that no value has follows the last code of the longest length
            writer.WriteBool(table.IsAc);
            writer.WriteBits((uint)table.Slot, 2);
            writer.WriteBool(table.IsLast);
            var last = Array.FindLastIndex(table.Counts, count => count != 0);
            writer.WriteU32(0, 0, 0, 1, 0, 2, 3, 0, 8);
            for (var i = 0; i < 16; i++)
            {
                writer.WriteU32((uint)(table.Counts[i] + (i == last ? 1 : 0)), 0, 0, 1, 0, 2, 3, 0, 8);
            }

            foreach (var value in table.Values)
            {
                writer.WriteU32(value, 0, 2, 4, 2, 8, 4, 1, 8);
            }

            writer.WriteU32(256, 0, 2, 4, 2, 8, 4, 1, 8);
        }

        foreach (var scan in model.Scans)
        {
            writer.WriteU32((uint)scan.Components.Count, 1, 0, 2, 0, 3, 0, 4, 0);
            writer.WriteBits((uint)scan.SpectralStart, 6);
            writer.WriteBits((uint)scan.SpectralEnd, 6);
            writer.WriteBits((uint)scan.SuccessiveLow, 4);
            writer.WriteBits((uint)scan.SuccessiveHigh, 4);
            foreach (var component in scan.Components)
            {
                writer.WriteBits((uint)component.Component, 2);
                writer.WriteBits((uint)component.AcTable, 2);
                writer.WriteBits((uint)component.DcTable, 2);
            }

            writer.WriteU32(0, 0, 0, 1, 0, 2, 0, 3, 3);
        }

        if (model.MarkerOrder.Contains(0xDD))
        {
            writer.WriteBits((uint)model.RestartInterval, 16);
        }

        foreach (var scan in model.Scans)
        {
            if (scan.ResetPoints.Count > 20 + ushort.MaxValue)
                throw TooMuch("that number of runs of blocks an encoder would not write");

            writer.WriteU32((uint)scan.ResetPoints.Count, 0, 0, 1, 2, 4, 4, 20, 16);
            var previous = -1;
            foreach (var block in scan.ResetPoints)
            {
                writer.WriteU32((uint)(block - previous - 1), 0, 0, 1, 3, 9, 5, 41, 28);
                previous = block;
            }

            if (scan.ExtraZeroRuns.Count > 20 + ushort.MaxValue)
                throw TooMuch("that number of blocks that end with runs of zeros");

            writer.WriteU32((uint)scan.ExtraZeroRuns.Count, 0, 0, 1, 2, 4, 4, 20, 16);
            previous = -1;
            foreach (var (block, count) in scan.ExtraZeroRuns)
            {
                if (count > 20 + 255)
                    throw TooMuch("that number of runs of zeros at the end of a block");

                writer.WriteU32((uint)count, 1, 0, 2, 2, 5, 4, 20, 8);
                writer.WriteU32((uint)(block - previous - 1), 0, 0, 1, 3, 9, 5, 41, 28);
                previous = block;
            }
        }

        foreach (var segment in model.InterMarkerSegments)
        {
            writer.WriteBits((uint)segment.Length, 16);
        }

        writer.WriteU32((uint)model.TailData.Length, 0, 0, 1, 8, 257, 16, 65793, 22);
        writer.WriteBool(model.PaddingBits is not null);
        if (model.PaddingBits is { } bits)
        {
            writer.WriteBits((uint)bits.Length, 24);
            foreach (var bit in bits)
            {
                writer.WriteBits(bit, 1);
            }
        }

        writer.ZeroPadToByte();

        // The segments the JPEG XL file does not hold elsewhere, one after the other, as a Brotli stream
        long total = model.TailData.Length;
        for (var i = 0; i < model.ApplicationSegments.Count; i++)
        {
            total += segmentKinds[i] == SegmentUnknown ? model.ApplicationSegments[i].Length : 0;
        }

        total += model.CommentSegments.Sum(segment => (long)segment.Length) + model.InterMarkerSegments.Sum(segment => (long)segment.Length);
        using var segments = Av1PooledArray<byte>.Rent(scope, total, clear: false, AllocationKind.Temporary);
        var span = segments.Span;
        var position = 0;
        for (var i = 0; i < model.ApplicationSegments.Count; i++)
        {
            if (segmentKinds[i] == SegmentUnknown)
            {
                model.ApplicationSegments[i].CopyTo(span[position..]);
                position += model.ApplicationSegments[i].Length;
            }
        }

        foreach (var segment in model.CommentSegments.Concat(model.InterMarkerSegments))
        {
            segment.CopyTo(span[position..]);
            position += segment.Length;
        }

        model.TailData.CopyTo(span[position..]);
        using var compressed = Av1PooledArray<byte>.Rent(scope, System.IO.Compression.BrotliEncoder.GetMaxCompressedLength((int)total), clear: false, AllocationKind.Temporary);
        if (!System.IO.Compression.BrotliEncoder.TryCompress(span, compressed.Span, out var compressedLength, brotliQuality, window: 22))
            throw new InvalidOperationException("The segments of the JPEG file could not be compressed.");

        var result = new byte[writer.Length + compressedLength];
        writer.WrittenSpan.CopyTo(result);
        compressed.Span[..compressedLength].CopyTo(result.AsSpan(writer.Length));
        return result;
    }

    private static UnsupportedImageFeatureException TooMuch(string what)
        => new($"The JPEG file cannot be recompressed: it has {what}, which the JPEG reconstruction data of a JPEG XL file cannot describe.", ImageFormat.Jpeg, "JPEG recompression: " + what);

    private static InvalidImageContentException Invalid(string message)
        => new("The JPEG reconstruction data of the JPEG XL file are invalid: " + message, ImageFormat.JpegXl);
}
