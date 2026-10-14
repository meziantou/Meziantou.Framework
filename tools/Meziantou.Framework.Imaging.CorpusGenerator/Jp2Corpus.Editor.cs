using System.Buffers.Binary;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Framework.Imaging.TestHarness.Jpeg2000;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>
/// Rearranges codestreams written by opj_compress into forms it does not write: packet headers packed in the main header
/// or in the tile-part headers, tile-parts of several tiles interleaved, marker segments of one component, marker
/// segments in a tile-part header. Nothing is decoded: marker segments and whole packets are moved, and the packets are
/// found by the SOP marker segments and the EPH markers the encoder was asked to write.
/// </summary>
internal static partial class Jp2Corpus
{
    private const int MarkerCod = 0x52;
    private const int MarkerCoc = 0x53;
    private const int MarkerQcd = 0x5C;
    private const int MarkerQcc = 0x5D;
    private const int MarkerPpm = 0x60;
    private const int MarkerPpt = 0x61;

    /// <summary>A tile-part: the marker segments of its header, between the SOT marker segment and the SOD marker, and its data.</summary>
    private sealed record TilePartContent(int Tile, int Part, int PartCount, byte[] Header, byte[] Data);

    private static (byte[] MainHeader, List<TilePartContent> TileParts) SplitCodestream(byte[] codestream)
    {
        var file = ReferenceJpeg2000.Parse(codestream);
        Py.Assert(!file.IsFile && file.HasEndOfCodestream, "a codestream");
        var parts = new List<TilePartContent>();
        foreach (var part in file.TileParts)
        {
            var dataStart = part.Offset + part.Length - part.DataLength;
            parts.Add(new TilePartContent(part.Tile, part.Part, part.PartCount, codestream[(part.Offset + 12)..(dataStart - 2)], codestream[dataStart..(part.Offset + part.Length)]));
        }

        return (codestream[..file.TileParts[0].Offset], parts);
    }

    private static byte[] JoinCodestream(byte[] mainHeader, IEnumerable<TilePartContent> tileParts)
    {
        var builder = new ByteBuilder().Bytes(mainHeader);
        foreach (var part in tileParts)
        {
            // SOT: Lsot, Isot, Psot (from the SOT marker to the end of the data), TPsot, TNsot; then the header and SOD
            builder.U8(0xFF).U8(0x90).U16BE(10).U16BE(part.Tile).U32BE(12 + part.Header.Length + 2 + part.Data.Length).U8(part.Part).U8(part.PartCount);
            builder.Bytes(part.Header).U8(0xFF).U8(0x93).Bytes(part.Data);
        }

        return builder.U8(0xFF).U8(0xD9).ToArray();
    }

    private static byte[] Segment(int marker, ReadOnlySpan<byte> content) => new ByteBuilder().U8(0xFF).U8(marker).U16BE(content.Length + 2).Bytes(content).ToArray();

    /// <summary>The content of the first marker segment of a type in a header: what follows its length.</summary>
    private static byte[] SegmentContent(ReadOnlySpan<byte> header, int marker)
    {
        var offset = FindMarkerSegment(header, marker);
        return header.Slice(offset + 4, BinaryPrimitives.ReadUInt16BigEndian(header[(offset + 2)..]) - 2).ToArray();
    }

    /// <summary>
    /// The packets of the data of a tile-part: each starts with an SOP marker segment (FF91, a length of 4, a sequence
    /// number) and its header ends with an EPH marker (FF92). Neither marker can appear in a packet header, whose bytes
    /// that follow FF are below 80, nor in the coding passes, where FF is followed by a byte of at most 8F.
    /// </summary>
    private static List<(byte[] Start, byte[] Header, byte[] Body)> SplitPackets(byte[] data)
    {
        var packets = new List<(byte[], byte[], byte[])>();
        var position = 0;
        while (position < data.Length)
        {
            Py.Assert(data.AsSpan(position).StartsWith<byte>([0xFF, 0x91, 0x00, 0x04]), "a packet starts with an SOP marker segment");
            var headerStart = position + 6;
            var headerEnd = Bytes.Find(data, [0xFF, 0x92], headerStart);
            Py.Assert(headerEnd >= 0, "a packet header ends with an EPH marker");
            var next = Bytes.Find(data, [0xFF, 0x91], headerEnd + 2);
            if (next < 0)
                next = data.Length;

            packets.Add((data[position..headerStart], data[headerStart..(headerEnd + 2)], data[(headerEnd + 2)..next]));
            position = next;
        }

        return packets;
    }

    /// <summary>
    /// Moves the packet headers, with their EPH markers, out of the data of the tile-parts: into a PPT marker segment of
    /// each tile-part header, or into one PPM marker segment of the main header (T.800, A.7.4 and A.7.5).
    /// </summary>
    private static byte[] PackHeaders(byte[] codestream, bool inMainHeader)
    {
        var (mainHeader, tileParts) = SplitCodestream(codestream);
        var packed = new ByteBuilder().U8(0);
        var result = new List<TilePartContent>();
        foreach (var part in tileParts)
        {
            var headers = new ByteBuilder();
            var bodies = new ByteBuilder();
            foreach (var (start, header, body) in SplitPackets(part.Data))
            {
                headers.Bytes(header);
                bodies.Bytes(start).Bytes(body);
            }

            if (inMainHeader)
            {
                // Zppm, then for each tile-part: Nppm, the number of bytes of its packet headers, and these bytes
                packed.U32BE(headers.Length).Bytes(headers.ToArray());
                result.Add(part with { Data = bodies.ToArray() });
            }
            else
            {
                // Zppt, then the packet headers of the tile-part
                result.Add(part with { Header = [.. part.Header, .. Segment(MarkerPpt, [0, .. headers.ToArray()])], Data = bodies.ToArray() });
            }
        }

        return JoinCodestream(inMainHeader ? [.. mainHeader, .. Segment(MarkerPpm, packed.ToArray())] : mainHeader, result);
    }

    /// <summary>
    /// Orders the tile-parts by their index in their tile, then by tile: the first tile-part of every tile, then the
    /// second one of every tile (T.800, A.4.2: the tile-parts of a tile are in order, those of several tiles may be
    /// interleaved).
    /// </summary>
    private static byte[] InterleaveTileParts(byte[] codestream)
    {
        var (mainHeader, tileParts) = SplitCodestream(codestream);
        Py.Assert(tileParts.Select(part => part.Tile).Distinct().Count() > 1 && tileParts.Exists(part => part.Part > 0), "several tiles of several tile-parts");
        return JoinCodestream(mainHeader, tileParts.OrderBy(part => part.Part).ThenBy(part => part.Tile));
    }

    /// <summary>
    /// Adds a COC and a QCC marker segment for a component to the main header: they repeat the coding style of the COD
    /// marker segment and the quantization of the QCD marker segment (T.800, A.6.2 and A.6.5).
    /// </summary>
    private static byte[] AddComponentSegments(byte[] codestream, int component)
    {
        var (mainHeader, tileParts) = SplitCodestream(codestream);
        var cod = SegmentContent(mainHeader, MarkerCod);
        var qcd = SegmentContent(mainHeader, MarkerQcd);

        // COD: Scod, SGcod (four bytes), SPcod. COC: Ccoc, Scoc (the precinct bit of Scod), SPcoc
        byte[] coc = [(byte)component, (byte)(cod[0] & 1), .. cod.AsSpan(5)];
        byte[] qcc = [(byte)component, .. qcd];
        return JoinCodestream([.. mainHeader, .. Segment(MarkerCoc, coc), .. Segment(MarkerQcc, qcc)], tileParts);
    }

    /// <summary>
    /// Repeats the COD and QCD marker segments of the main header in the header of the first tile-part of each tile, and
    /// replaces those of the main header by a coding style and a quantization that no tile uses: one decomposition level
    /// less. A decoder that ignores the marker segments of the tile-part headers reads other sub-bands.
    /// </summary>
    private static byte[] MoveCodingStyleToTileParts(byte[] codestream)
    {
        var (mainHeader, tileParts) = SplitCodestream(codestream);
        var cod = FindMarkerSegment(mainHeader, MarkerCod);
        var qcd = FindMarkerSegment(mainHeader, MarkerQcd);
        var codSegment = mainHeader.AsSpan(cod, 2 + BinaryPrimitives.ReadUInt16BigEndian(mainHeader.AsSpan(cod + 2))).ToArray();
        var qcdSegment = mainHeader.AsSpan(qcd, 2 + BinaryPrimitives.ReadUInt16BigEndian(mainHeader.AsSpan(qcd + 2))).ToArray();
        Py.Assert((codSegment[4] & 1) == 0 && (qcdSegment[4] & 0x1F) == 0 && codSegment[9] >= 1, "default precincts, no quantization, a decomposition level");

        // One decomposition level less: SPcod, and three exponents less in the QCD marker segment
        var header = (byte[])mainHeader.Clone();
        header[cod + 9]--;
        var shorter = qcdSegment.AsSpan(0, qcdSegment.Length - 3).ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(shorter.AsSpan(2), (ushort)(shorter.Length - 2));
        byte[] newHeader = [.. header.AsSpan(0, qcd), .. shorter, .. header.AsSpan(qcd + qcdSegment.Length)];
        return JoinCodestream(newHeader, tileParts.Select(part => part.Part == 0 ? part with { Header = [.. codSegment, .. qcdSegment, .. part.Header] } : part));
    }
}
