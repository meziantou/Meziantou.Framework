namespace Meziantou.Framework.MediaTags.Formats.Ogg;

internal static class OggPacketUtilities
{
    /// <summary>The number of leading pages searched for a header packet.</summary>
    /// <remarks>
    /// The identification, comment and setup packets are the first packets of a logical stream, so reading the
    /// tags never needs more than the first few pages. Reading the whole file instead would cost a full read
    /// and several copies of the audio for an answer that is always at the front.
    /// </remarks>
    private const int HeaderSearchPageLimit = 64;

    /// <summary>The size of the window searched backwards for the last page of the file.</summary>
    private const int LastPageSearchWindow = 128 * 1024;

    /// <summary>
    /// Finds the first packet of the first logical stream that starts with <paramref name="prefix"/>.
    /// </summary>
    public static bool TryFindHeaderPacket(Stream stream, ReadOnlySpan<byte> prefix, [NotNullWhen(true)] out byte[]? packet)
    {
        packet = null;

        uint? serialNumber = null;
        using var currentPacket = new MemoryStream();

        for (var pageIndex = 0; pageIndex < HeaderSearchPageLimit; pageIndex++)
        {
            var page = OggPage.Read(stream);
            if (page is null)
                return false;

            serialNumber ??= page.SerialNumber;
            if (page.SerialNumber != serialNumber)
                continue;

            var dataOffset = 0;
            foreach (var segmentLength in page.SegmentTable)
            {
                if (segmentLength > 0)
                    currentPacket.Write(page.Data, dataOffset, segmentLength);

                dataOffset += segmentLength;

                if (segmentLength < 255)
                {
                    var completed = currentPacket.GetBuffer().AsSpan(0, (int)currentPacket.Length);
                    if (completed.StartsWith(prefix))
                    {
                        packet = completed.ToArray();
                        return true;
                    }

                    currentPacket.SetLength(0);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the granule position of the last page of the given logical stream, by searching backwards from the
    /// end of the file rather than reading every page.
    /// </summary>
    public static bool TryGetLastGranulePosition(Stream stream, uint serialNumber, out long granulePosition)
    {
        granulePosition = -1;

        var length = stream.Length;
        var windowStart = Math.Max(0, length - LastPageSearchWindow);
        var windowLength = (int)(length - windowStart);
        if (windowLength < OggPage.FixedHeaderSize)
            return false;

        var window = new byte[windowLength];
        stream.Position = windowStart;
        if (stream.ReadAtLeast(window, windowLength, throwOnEndOfStream: false) < windowLength)
            return false;

        for (var i = windowLength - OggPage.FixedHeaderSize; i >= 0; i--)
        {
            if (window[i] != 'O' || window[i + 1] != 'g' || window[i + 2] != 'g' || window[i + 3] != 'S')
                continue;

            stream.Position = windowStart + i;
            var page = OggPage.Read(stream);

            // The audio can contain the capture pattern by chance, so a candidate is only accepted once its
            // checksum confirms it really is a page.
            if (page is null || !page.VerifyChecksum() || page.SerialNumber != serialNumber || page.GranulePosition < 0)
                continue;

            granulePosition = page.GranulePosition;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Reads every page of the stream.
    /// </summary>
    public static List<OggPage> ReadAllPages(Stream stream)
    {
        // A page header is at least 27 bytes, so this can never reject a file that is actually made of pages.
        var maxPageCount = (stream.CanSeek ? stream.Length / OggPage.FixedHeaderSize : int.MaxValue) + 16;

        var pages = new List<OggPage>();
        while (true)
        {
            if (pages.Count > maxPageCount)
                throw new InvalidDataException("The file declares more OGG pages than it has room for.");

            var page = OggPage.Read(stream);
            if (page is null)
                break;

            pages.Add(page);
        }

        return pages;
    }

    /// <summary>
    /// Returns the serial number shared by every page, or <see langword="null"/> when the file carries more
    /// than one logical stream.
    /// </summary>
    /// <remarks>
    /// Rewriting one stream of a multiplexed or chained file requires renumbering only that stream's pages and
    /// leaving the others interleaved where they are. That is not implemented, and doing it wrong leaves a
    /// sequence hole in a stream this library never meant to touch, so those files are refused instead.
    /// </remarks>
    public static uint? GetSingleStreamSerialNumber(IReadOnlyList<OggPage> pages)
    {
        if (pages.Count == 0)
            return null;

        var serialNumber = pages[0].SerialNumber;
        foreach (var page in pages)
        {
            if (page.SerialNumber != serialNumber)
                return null;
        }

        return serialNumber;
    }

    public static List<OggPacketInfo> ReadPackets(IReadOnlyList<OggPage> pages)
    {
        var maxPacketCount = GetMaxPacketCount(pages);

        var packets = new List<OggPacketInfo>();
        using var currentPacket = new MemoryStream();

        var hasCurrentPacket = false;
        var packetStartPageIndex = 0;
        var packetStartsAtPageStart = false;

        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            var page = pages[pageIndex];
            var dataOffset = 0;

            for (var segmentIndex = 0; segmentIndex < page.SegmentTable.Length; segmentIndex++)
            {
                var segmentLength = page.SegmentTable[segmentIndex];
                if (!hasCurrentPacket)
                {
                    hasCurrentPacket = true;
                    packetStartPageIndex = pageIndex;
                    packetStartsAtPageStart = segmentIndex == 0 && (page.HeaderType & OggPage.HeaderTypeContinued) == 0;
                }

                if (segmentLength > 0)
                {
                    currentPacket.Write(page.Data, dataOffset, segmentLength);
                }

                dataOffset += segmentLength;

                if (segmentLength < 255)
                {
                    // A page can declare 255 zero-length segments for 282 bytes of input, so without a bound
                    // a small file can force an arbitrarily large object graph.
                    if (packets.Count >= maxPacketCount)
                        throw new InvalidDataException("The file declares more OGG packets than it has room for.");

                    packets.Add(new OggPacketInfo
                    {
                        Data = currentPacket.ToArray(),
                        StartPageIndex = packetStartPageIndex,
                        EndPageIndex = pageIndex,
                        StartsAtPageStart = packetStartsAtPageStart,
                        EndsAtPageEnd = segmentIndex == page.SegmentTable.Length - 1,
                        FinalPageGranulePosition = page.GranulePosition,
                    });

                    currentPacket.SetLength(0);
                    hasCurrentPacket = false;
                }
            }
        }

        if (hasCurrentPacket)
            throw new InvalidDataException("Invalid OGG stream: unterminated packet.");

        return packets;
    }

    private static long GetMaxPacketCount(IReadOnlyList<OggPage> pages)
    {
        var totalBytes = 0L;
        foreach (var page in pages)
        {
            totalBytes += OggPage.FixedHeaderSize + page.SegmentTable.Length + page.Data.Length;
        }

        // Real audio packets are far larger than eight bytes, so this bounds the damage a file made only of
        // empty segments can do without rejecting anything legitimate.
        return Math.Max(65536, totalBytes / 8);
    }

    public static List<OggPage> ReplacePacket(
        IReadOnlyList<OggPage> pages,
        ReadOnlySpan<byte> packetPrefix,
        byte[] replacementPacketData)
    {
        var packets = ReadPackets(pages);
        var packetIndexToReplace = FindPacketIndex(packets, packetPrefix);
        if (packetIndexToReplace < 0)
            throw new InvalidDataException("Target OGG packet not found.");

        var rewriteStartPacketIndex = packetIndexToReplace;
        while (rewriteStartPacketIndex > 0 && !packets[rewriteStartPacketIndex].StartsAtPageStart)
        {
            rewriteStartPacketIndex--;
        }

        var rewriteEndPacketIndex = packets.Count;
        for (var i = packetIndexToReplace + 1; i < packets.Count; i++)
        {
            if (packets[i].StartsAtPageStart)
            {
                rewriteEndPacketIndex = i;
                break;
            }
        }

        var rewriteStartPageIndex = packets[rewriteStartPacketIndex].StartPageIndex;
        var appendStartPageIndex = rewriteEndPacketIndex < packets.Count ? packets[rewriteEndPacketIndex].StartPageIndex : pages.Count;

        var packetsToRewrite = new List<(byte[] Data, long GranulePosition)>(rewriteEndPacketIndex - rewriteStartPacketIndex);
        for (var i = rewriteStartPacketIndex; i < rewriteEndPacketIndex; i++)
        {
            var packet = packets[i];
            var data = i == packetIndexToReplace ? replacementPacketData : packet.Data;
            var granulePosition = packet.EndsAtPageEnd ? packet.FinalPageGranulePosition : -1;
            packetsToRewrite.Add((data, granulePosition));
        }

        var templatePage = pages[rewriteStartPageIndex];
        var includeBeginOfStream = rewriteStartPageIndex == 0 && (pages[0].HeaderType & OggPage.HeaderTypeBeginOfStream) != 0;
        var includeEndOfStream = appendStartPageIndex == pages.Count && (pages[^1].HeaderType & OggPage.HeaderTypeEndOfStream) != 0;
        var rebuiltPages = BuildPagesFromPackets(
            packetsToRewrite,
            templatePage.Version,
            templatePage.SerialNumber,
            templatePage.PageSequenceNumber,
            includeBeginOfStream,
            includeEndOfStream);

        var outputPages = new List<OggPage>(rewriteStartPageIndex + rebuiltPages.Count + pages.Count - appendStartPageIndex);

        // The pages outside the rewrite window are reused rather than copied: this list is built to be written
        // out immediately and nothing else observes them.
        for (var i = 0; i < rewriteStartPageIndex; i++)
        {
            outputPages.Add(pages[i]);
        }

        outputPages.AddRange(rebuiltPages);

        var replacedPageCount = appendStartPageIndex - rewriteStartPageIndex;
        var sequenceDelta = rebuiltPages.Count - replacedPageCount;
        for (var i = appendStartPageIndex; i < pages.Count; i++)
        {
            var page = pages[i];
            var newSequence = (long)page.PageSequenceNumber + sequenceDelta;
            if (newSequence is < 0 or > uint.MaxValue)
                throw new InvalidDataException("Invalid OGG sequence number after packet rewrite.");

            page.PageSequenceNumber = (uint)newSequence;
            outputPages.Add(page);
        }

        return outputPages;
    }

    private static List<OggPage> BuildPagesFromPackets(
        IReadOnlyList<(byte[] Data, long GranulePosition)> packets,
        byte version,
        uint serialNumber,
        uint firstSequenceNumber,
        bool includeBeginOfStreamOnFirstPage,
        bool includeEndOfStreamOnLastPage)
    {
        var outputPages = new List<OggPage>();
        var sequenceNumber = firstSequenceNumber;
        var isFirstOutputPage = true;

        foreach (var packet in packets)
        {
            var lacingValues = BuildLacingValues(packet.Data.Length);
            var lacingOffset = 0;
            var dataOffset = 0;
            var isFirstChunkOfPacket = true;

            while (lacingOffset < lacingValues.Count)
            {
                var lacingCount = Math.Min(255, lacingValues.Count - lacingOffset);
                var segmentTable = new byte[lacingCount];
                lacingValues.CopyTo(lacingOffset, segmentTable, 0, lacingCount);

                var pageDataLength = 0;
                for (var i = 0; i < segmentTable.Length; i++)
                {
                    pageDataLength += segmentTable[i];
                }

                var pageData = new byte[pageDataLength];
                if (pageDataLength > 0)
                {
                    Array.Copy(packet.Data, dataOffset, pageData, 0, pageDataLength);
                }

                dataOffset += pageDataLength;
                lacingOffset += lacingCount;

                var isLastChunkOfPacket = lacingOffset >= lacingValues.Count;

                var headerType = 0;
                if (isFirstOutputPage && includeBeginOfStreamOnFirstPage)
                {
                    headerType |= OggPage.HeaderTypeBeginOfStream;
                }

                if (!isFirstChunkOfPacket)
                {
                    headerType |= OggPage.HeaderTypeContinued;
                }

                outputPages.Add(new OggPage
                {
                    Version = version,
                    HeaderType = (byte)headerType,
                    GranulePosition = isLastChunkOfPacket ? packet.GranulePosition : -1,
                    SerialNumber = serialNumber,
                    PageSequenceNumber = sequenceNumber++,
                    SegmentTable = segmentTable,
                    Data = pageData,
                });

                isFirstOutputPage = false;
                isFirstChunkOfPacket = false;
            }
        }

        if (includeEndOfStreamOnLastPage && outputPages.Count > 0)
        {
            outputPages[^1].HeaderType |= OggPage.HeaderTypeEndOfStream;
        }

        return outputPages;
    }

    private static List<byte> BuildLacingValues(int dataLength)
    {
        var lacingValues = new List<byte>();
        if (dataLength == 0)
        {
            lacingValues.Add(0);
            return lacingValues;
        }

        var remaining = dataLength;
        while (remaining >= 255)
        {
            lacingValues.Add(255);
            remaining -= 255;
        }

        lacingValues.Add((byte)remaining);
        return lacingValues;
    }

    private static int FindPacketIndex(List<OggPacketInfo> packets, ReadOnlySpan<byte> prefix)
    {
        for (var i = 0; i < packets.Count; i++)
        {
            if (packets[i].Data.AsSpan().StartsWith(prefix))
                return i;
        }

        return -1;
    }
}
