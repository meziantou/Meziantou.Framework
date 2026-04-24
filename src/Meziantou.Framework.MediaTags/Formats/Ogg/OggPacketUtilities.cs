namespace Meziantou.Framework.MediaTags.Formats.Ogg;

internal sealed class OggPacketInfo
{
    public required byte[] Data { get; init; }
    public required int StartPageIndex { get; init; }
    public required int EndPageIndex { get; init; }
    public required bool StartsAtPageStart { get; init; }
    public required bool EndsAtPageEnd { get; init; }
    public required long FinalPageGranulePosition { get; init; }
}

internal static class OggPacketUtilities
{
    public static List<OggPage> ReadAllPages(Stream stream)
    {
        var pages = new List<OggPage>();
        while (true)
        {
            var page = OggPage.Read(stream);
            if (page is null)
                break;

            pages.Add(page);
        }

        return pages;
    }

    public static List<OggPacketInfo> ReadPackets(IReadOnlyList<OggPage> pages)
    {
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
            throw new InvalidOperationException("Invalid OGG stream: unterminated packet.");

        return packets;
    }

    public static List<OggPage> ReplacePacket(
        IReadOnlyList<OggPage> pages,
        ReadOnlySpan<byte> packetPrefix,
        byte[] replacementPacketData)
    {
        var packets = ReadPackets(pages);
        var packetIndexToReplace = FindPacketIndex(packets, packetPrefix);
        if (packetIndexToReplace < 0)
            throw new InvalidOperationException("Target OGG packet not found.");

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
        for (var i = 0; i < rewriteStartPageIndex; i++)
        {
            outputPages.Add(pages[i].Clone());
        }

        outputPages.AddRange(rebuiltPages);

        var replacedPageCount = appendStartPageIndex - rewriteStartPageIndex;
        var sequenceDelta = rebuiltPages.Count - replacedPageCount;
        for (var i = appendStartPageIndex; i < pages.Count; i++)
        {
            var page = pages[i].Clone();
            var newSequence = (long)page.PageSequenceNumber + sequenceDelta;
            if (newSequence is < 0 or > uint.MaxValue)
                throw new InvalidOperationException("Invalid OGG sequence number after packet rewrite.");

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

    private static int FindPacketIndex(IReadOnlyList<OggPacketInfo> packets, ReadOnlySpan<byte> prefix)
    {
        for (var i = 0; i < packets.Count; i++)
        {
            if (packets[i].Data.AsSpan().StartsWith(prefix))
                return i;
        }

        return -1;
    }
}
