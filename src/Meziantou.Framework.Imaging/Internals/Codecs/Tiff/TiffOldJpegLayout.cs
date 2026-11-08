namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// How the blocks of a page compressed with old-style JPEG (TIFF 6.0 section 22, <c>Compression</c> 6) become JPEG
/// datastreams that the JPEG decoder of the library reads, following what libtiff does with such files.
/// </summary>
/// <remarks>
/// <para>
/// TIFF 6.0 defined this compression loosely and TIFF Technical Note 2 replaced it, so its files come in two forms:
/// </para>
/// <list type="bullet">
/// <item><description>
/// With <c>JPEGInterchangeFormat</c>, the offset of a JPEG datastream: its markers up to the first start of scan are the
/// header of the image, and the strips or tiles hold the entropy-coded data. As libtiff reads them, each block is coded
/// on its own (its DC predictions start from 0, as after a restart marker), so the datastream of a block is the header
/// with the height of the block, followed by the data of the block. Without strips, the scan is what follows the header
/// in the interchange format, and the page is decoded as a single datastream (<see cref="IsWholeImage"/>).
/// </description></item>
/// <item><description>
/// Without it, the tables are in tags (<c>JPEGQTables</c>, <c>JPEGDCTables</c>, <c>JPEGACTables</c>,
/// <c>JPEGRestartInterval</c>) and each block holds the entropy-coded data of one frame of the size of the block: the
/// header is assembled from the tags, with the sampling factors of <c>YCbCrSubSampling</c> for YCbCr data. A block that
/// starts with a start-of-image marker is a datastream of its own.
/// </description></item>
/// </list>
/// <para>Only the baseline process (<c>JPEGProc</c> 1) is supported, with 8-bit samples.</para>
/// </remarks>
internal sealed class TiffOldJpegLayout
{
    private const byte MarkerSoi = 0xD8;
    private const byte MarkerEoi = 0xD9;
    private const byte MarkerSos = 0xDA;
    private const byte MarkerDqt = 0xDB;
    private const byte MarkerDht = 0xC4;
    private const byte MarkerDri = 0xDD;

    private TiffOldJpegLayout(byte[][] headers, int heightOffset, bool isWholeImage, (ulong Offset, ulong Length)[] dataRanges)
    {
        Headers = headers;
        HeightOffset = heightOffset;
        IsWholeImage = isWholeImage;
        DataRanges = dataRanges;
    }

    /// <summary>
    /// Gets the markers that start the datastream of a block, up to the end of the start-of-scan segment: one header,
    /// or one per plane of a planar page.
    /// </summary>
    public byte[][] Headers { get; }

    /// <summary>Gets the offset of the number of lines of the frame header in <see cref="Headers"/>, which is set to the height of each block.</summary>
    public int HeightOffset { get; }

    /// <summary>
    /// Gets a value indicating whether the page is decoded as a single datastream: <see cref="Headers"/> followed by every
    /// range of <see cref="DataRanges"/>. The page then has a single block, as tall as the page.
    /// </summary>
    public bool IsWholeImage { get; }

    /// <summary>Gets the ranges of the input that hold the entropy-coded data of a page decoded as a single datastream, in order.</summary>
    public (ulong Offset, ulong Length)[] DataRanges { get; }

    /// <summary>Creates the layout of a page whose header is the start of the datastream at <c>JPEGInterchangeFormat</c>.</summary>
    /// <param name="page">The page; its blocks are resolved.</param>
    /// <param name="interchange">The bytes at <c>JPEGInterchangeFormat</c>: at least its markers up to the end of the first start-of-scan segment.</param>
    /// <param name="interchangeOffset">The offset of the datastream in the input.</param>
    /// <param name="interchangeLength">The length of the datastream (<c>JPEGInterchangeFormatLength</c>), or <see langword="null"/>.</param>
    /// <param name="hasBlocks">Whether the page declares its strips or tiles (offsets and byte counts).</param>
    public static TiffOldJpegLayout FromInterchangeFormat(TiffPage page, ReadOnlySpan<byte> interchange, ulong interchangeOffset, ulong? interchangeLength, bool hasBlocks)
    {
        var (headerLength, heightOffset, frameHeight) = ParseHeader(page, interchange);
        var header = interchange[..headerLength].ToArray();
        if (!hasBlocks)
        {
            // The scan follows the header in the interchange format
            if (interchangeLength is not { } length || length <= (ulong)headerLength)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The old-style JPEG page {page.Index} declares neither its strips nor the length of its JPEG interchange format."));

            return new TiffOldJpegLayout([header], heightOffset, isWholeImage: true, [(interchangeOffset + (ulong)headerLength, length - (ulong)headerLength)]);
        }

        if (page.IsPlanar)
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The old-style JPEG page {page.Index} stores separate planes with the header of its JPEG interchange format, whose frame codes every component."), "Compression: old-style JPEG planes with an interchange format");

        // libtiff requires the frame of the page; the one of a block describes the same blocks
        if (frameHeight != page.Size.Height && frameHeight != page.BlockHeight)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG interchange format of the old-style JPEG page {page.Index} declares {frameHeight} lines; the page has {page.Size.Height} and its blocks {page.BlockHeight}."));

        return new TiffOldJpegLayout([header], heightOffset, isWholeImage: false, []);
    }

    /// <summary>Creates the layout of a page whose tables are in tags: one header per plane, assembled from the tags.</summary>
    /// <param name="page">The page; its blocks are resolved.</param>
    /// <param name="quantizationTables">The 64 values of the quantization table of each component, in zigzag order.</param>
    /// <param name="dcTables">The 16 code counts followed by the values of the DC Huffman table of each component.</param>
    /// <param name="acTables">The 16 code counts followed by the values of the AC Huffman table of each component.</param>
    /// <param name="tableOffsets">The offsets the tables of each component were read from (quantization, DC, AC): components that share them share a table.</param>
    /// <param name="restartInterval">The restart interval, 0 for none.</param>
    public static TiffOldJpegLayout FromTables(TiffPage page, byte[][] quantizationTables, byte[][] dcTables, byte[][] acTables, (ulong Quantization, ulong Dc, ulong Ac)[] tableOffsets, int restartInterval)
    {
        var componentCount = page.SamplesPerPixel;
        var (horizontal, vertical) = page.YCbCr is { } ycbcr && !page.IsPlanar ? (ycbcr.HorizontalSubsampling, ycbcr.VerticalSubsampling) : (1, 1);

        // Components that read their tables at the same offset share one table, as the writers meant
        var quantizationIndexes = AssignIndexes([.. tableOffsets.Select(static offsets => offsets.Quantization)]);
        var dcIndexes = AssignIndexes([.. tableOffsets.Select(static offsets => offsets.Dc)]);
        var acIndexes = AssignIndexes([.. tableOffsets.Select(static offsets => offsets.Ac)]);

        var headers = new byte[page.PlaneCount][];
        var heightOffset = 0;
        for (var plane = 0; plane < headers.Length; plane++)
        {
            int[] components = page.IsPlanar ? [plane] : [.. Enumerable.Range(0, componentCount)];
            var header = new List<byte> { 0xFF, MarkerSoi };
            foreach (var index in components.Select(component => quantizationIndexes[component]).Distinct())
            {
                var component = Array.IndexOf(quantizationIndexes, index);
                AddSegment(header, MarkerDqt, [(byte)index, .. quantizationTables[component]]);
            }

            foreach (var index in components.Select(component => dcIndexes[component]).Distinct())
            {
                AddSegment(header, MarkerDht, [(byte)index, .. dcTables[Array.IndexOf(dcIndexes, index)]]);
            }

            foreach (var index in components.Select(component => acIndexes[component]).Distinct())
            {
                AddSegment(header, MarkerDht, [(byte)(0x10 | index), .. acTables[Array.IndexOf(acIndexes, index)]]);
            }

            if (restartInterval > 0)
            {
                AddSegment(header, MarkerDri, [(byte)(restartInterval >> 8), (byte)restartInterval]);
            }

            // The baseline frame allows two Huffman tables of each class; the extended one, with the same coding, four
            var isBaseline = components.All(component => dcIndexes[component] < 2 && acIndexes[component] < 2);
            var frame = new List<byte> { 8, 0, 0, (byte)(page.BlockWidth >> 8), (byte)page.BlockWidth, (byte)components.Length };
            for (var i = 0; i < components.Length; i++)
            {
                var sampling = i == 0 ? (horizontal << 4) | vertical : 0x11;
                frame.AddRange([(byte)(components[i] + 1), (byte)sampling, (byte)quantizationIndexes[components[i]]]);
            }

            heightOffset = header.Count + 5;
            AddSegment(header, isBaseline ? (byte)0xC0 : (byte)0xC1, [.. frame]);

            var scan = new List<byte> { (byte)components.Length };
            foreach (var component in components)
            {
                scan.AddRange([(byte)(component + 1), (byte)((dcIndexes[component] << 4) | acIndexes[component])]);
            }

            scan.AddRange([0, 63, 0]);
            AddSegment(header, MarkerSos, [.. scan]);
            headers[plane] = [.. header];
        }

        return new TiffOldJpegLayout(headers, heightOffset, isWholeImage: false, []);
    }

    /// <summary>
    /// Assembles the datastream of a block: its header with the height of the block, then its data and an end-of-image
    /// marker. A block that is a datastream of its own is returned as it is.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="index">The index of the block.</param>
    /// <param name="rowCount">The number of rows of the block that belong to the page.</param>
    /// <param name="data">The bytes of the block.</param>
    /// <param name="destination">The buffer of at least <see cref="GetBlockDatastreamLength"/> bytes the datastream is written to.</param>
    /// <returns>The number of bytes written, or 0 when <paramref name="data"/> is the datastream.</returns>
    public int WriteBlockDatastream(TiffPage page, int index, int rowCount, ReadOnlySpan<byte> data, Span<byte> destination)
    {
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == MarkerSoi)
            return 0;

        var header = Headers[page.IsPlanar ? index / page.BlocksPerPlane : 0];
        header.CopyTo(destination);

        // A tile is always coded in full; a strip has the rows of the page it covers
        var height = page.IsTiled ? page.BlockHeight : rowCount;
        destination[HeightOffset] = (byte)(height >> 8);
        destination[HeightOffset + 1] = (byte)height;
        data.CopyTo(destination[header.Length..]);
        var length = header.Length + data.Length;
        destination[length] = 0xFF;
        destination[length + 1] = MarkerEoi;
        return length + 2;
    }

    /// <summary>Gets the length of the datastream <see cref="WriteBlockDatastream"/> writes for a block of <paramref name="dataLength"/> bytes.</summary>
    public int GetBlockDatastreamLength(TiffPage page, int index, int dataLength) => Headers[page.IsPlanar ? index / page.BlocksPerPlane : 0].Length + dataLength + 2;

    /// <summary>Finds the end of the first start-of-scan segment of the beginning of a JPEG interchange format.</summary>
    /// <param name="data">The first bytes of the interchange format.</param>
    /// <returns>The length of the header; 0 when <paramref name="data"/> ends before it; -1 when it is malformed.</returns>
    public static int FindHeaderLength(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
            return 0;

        if (data[0] != 0xFF || data[1] != MarkerSoi)
            return -1;

        var position = 2;
        while (true)
        {
            if (position + 2 > data.Length)
                return 0;

            if (data[position] != 0xFF)
                return -1;

            var marker = data[position + 1];
            if (marker == 0xFF)
            {
                position++;
                continue;
            }

            if (marker is MarkerEoi or MarkerSoi or (>= 0xD0 and <= 0xD7))
                return -1;

            if (position + 4 > data.Length)
                return 0;

            var length = (data[position + 2] << 8) | data[position + 3];
            if (length < 2)
                return -1;

            position += 2 + length;
            if (position > data.Length)
                return 0;

            if (marker == MarkerSos)
                return position;
        }
    }

    /// <summary>
    /// Reads the markers of a JPEG interchange format up to the end of its first start-of-scan segment, and checks its
    /// frame against the page.
    /// </summary>
    private static (int HeaderLength, int HeightOffset, int FrameHeight) ParseHeader(TiffPage page, ReadOnlySpan<byte> data)
    {
        if (data.Length < 2 || data[0] != 0xFF || data[1] != MarkerSoi)
            throw Invalid(page, "does not start with a start-of-image marker");

        var position = 2;
        var heightOffset = -1;
        var frameHeight = 0;
        while (true)
        {
            if (position + 2 > data.Length || data[position] != 0xFF)
                throw Invalid(page, "has no start-of-scan marker after its tables");

            var marker = data[position + 1];
            if (marker == 0xFF)
            {
                // Fill byte before a marker
                position++;
                continue;
            }

            if (marker is MarkerEoi or MarkerSoi or (>= 0xD0 and <= 0xD7) || position + 4 > data.Length)
                throw Invalid(page, "has no start-of-scan marker after its tables");

            var length = (data[position + 2] << 8) | data[position + 3];
            if (length < 2 || position + 2 + length > data.Length)
                throw Invalid(page, "has a truncated marker segment");

            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                // A frame header: precision, lines, samples per line, components
                if (length < 8)
                    throw Invalid(page, "has a truncated frame header");

                heightOffset = position + 5;
                frameHeight = (data[position + 5] << 8) | data[position + 6];
            }

            position += 2 + length;
            if (marker == MarkerSos)
            {
                if (heightOffset < 0)
                    throw Invalid(page, "has a start of scan before its frame header");

                return (position, heightOffset, frameHeight);
            }
        }
    }

    private static int[] AssignIndexes(ulong[] offsets)
    {
        var distinct = offsets.Distinct().ToArray();
        if (distinct.Length > 4)
            throw new InvalidOperationException("At most four tables of each kind are assigned.");

        return [.. offsets.Select(offset => Array.IndexOf(distinct, offset))];
    }

    private static void AddSegment(List<byte> destination, byte marker, byte[] payload)
    {
        destination.AddRange([0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2)]);
        destination.AddRange(payload);
    }

    private static InvalidImageContentException Invalid(TiffPage page, string reason)
        => TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG interchange format of the old-style JPEG page {page.Index} {reason}."));
}
