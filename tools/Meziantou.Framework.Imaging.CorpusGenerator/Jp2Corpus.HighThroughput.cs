using System.Buffers.Binary;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Framework.Imaging.TestHarness.Jpeg2000;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>
/// Hand-authored codestreams of the high-throughput block coder (Rec. ITU-T T.814 | ISO/IEC 15444-15). No pinned encoder
/// writes an HT SigProp pass, an HT MagRef pass, placeholder passes or several layers, so these are written here, from the
/// Recommendation.
/// </summary>
/// <remarks>
/// <para>
/// A codestream has one gray component of 8 bits, no wavelet transformation and one code-block: a sample is its
/// coefficient plus 128, so that the expected image is the coefficients the generator codes, not what a decoder returns.
/// </para>
/// <para>
/// The HT cleanup segment is the one ojph_compress writes for the image of the coefficients halved toward zero: with
/// refinement passes, the HT cleanup pass is one bit-plane higher (the packet header gives one zero bit-plane less), and
/// the coefficients it codes are the halves. The packet headers (T.800 B.10, T.814 Annex B) and the HT refinement segment
/// (T.814 7.1.5, 7.1.6, 7.4, 7.5) are written by this file.
/// </para>
/// </remarks>
internal static partial class Jp2Corpus
{
    /// <summary>A hand-authored codestream, the coefficients it codes and the samples a decoder shows.</summary>
    private sealed record Authored(Encoded Encoded, int[] Coefficients, Img Frame, int SignificancePropagationBits, int MagnitudeRefinementBits);

    /// <summary>How the coding passes of the code-block are arranged in the packets.</summary>
    private enum AuthoredPackets
    {
        /// <summary>One layer: every coding pass is in one packet.</summary>
        OnePacket,

        /// <summary>Two layers: the HT cleanup pass, then the HT refinement passes.</summary>
        RefinementInSecondPacket,

        /// <summary>Three layers: the HT cleanup pass, the HT SigProp pass, the HT MagRef pass.</summary>
        OnePassPerPacket,

        /// <summary>Two layers: the three coding passes of the HT set are followed by an HT cleanup pass whose segment is empty (T.814, B.3, NOTE 2).</summary>
        SecondSetInSecondPacket,

        /// <summary>Two layers: three placeholder passes and no byte, then the HT cleanup pass (T.814, B.3, NOTE 4).</summary>
        PlaceholdersInFirstPacket,
    }

    /// <summary>
    /// Writes a codestream whose code-block has an HT cleanup pass, and optionally an HT SigProp pass, an HT MagRef pass
    /// and placeholder passes.
    /// </summary>
    /// <param name="coefficients">The coefficients to code, row by row; a coefficient of magnitude 1 that the HT SigProp pass cannot reach is coded as 0.</param>
    /// <param name="passes">The number of coding passes of the HT set: 1 to 3.</param>
    /// <param name="placeholderSets">The number of groups of three placeholder passes that precede the HT set.</param>
    /// <param name="isVerticallyCausal">Whether the code-block style has the vertically causal contexts.</param>
    private static Authored AuthorHighThroughput(Corpus<Jp2Tools> c, int width, int height, List<int> coefficients, int passes, int placeholderSets = 0, bool isVerticallyCausal = false,
        AuthoredPackets packets = AuthoredPackets.OnePacket)
    {
        Py.Assert(width <= 64 && height <= 64 && coefficients.Count == width * height, "one code-block");
        Py.Assert(coefficients.All(value => Math.Abs(value) < 64), "the halves fit in the bit-planes ojph_compress leaves");
        var (coded, significancePropagation, magnitudeRefinement) = PlanRefinement(coefficients, width, height, isVerticallyCausal);
        if (passes == 1)
        {
            // The HT cleanup pass alone codes every bit of the coefficients
            coded = [.. coefficients];
        }

        // The HT cleanup segment, from ojph_compress
        var cleanupValues = passes == 1 ? coded : [.. coded.Select(value => (Math.Abs(value) >> 1) * Math.Sign(value))];
        var source = new Planes(width, height, 8, [[.. cleanupValues.Select(value => 128 + value)]]);
        var written = Ojph(c, source, isReversible: true, ["-num_decomps", "0"]);
        var data = written.Data;
        var file = ReferenceJpeg2000.Parse(data);
        Py.Assert(file.TileParts.Count == 1 && file.LayerCount == 1 && file.CodingStyles[0] is { DecompositionLevels: 0, CodeBlockStyle: 0x40 }, "the codestream ojph_compress writes");
        var tilePart = file.TileParts[0];
        var body = tilePart.Offset + tilePart.Length - tilePart.DataLength;
        var reader = new PacketBitReader(data, body);
        Py.Assert(reader.ReadBit() == 1 && reader.ReadBit() == 1, "a packet with the code-block");
        var zeroBitPlanes = 0;
        while (reader.ReadBit() == 0)
            zeroBitPlanes++;

        Py.Assert(reader.ReadBit() == 0, "one coding pass");
        var lengthBits = 3;
        while (reader.ReadBit() == 1)
            lengthBits++;

        var cleanupLength = reader.ReadBits(lengthBits);
        var cleanupStart = reader.Align();
        Py.Assert(cleanupStart + cleanupLength == tilePart.Offset + tilePart.Length, "the packet is the HT cleanup segment");
        var cleanup = data.AsSpan(cleanupStart, cleanupLength).ToArray();

        // The HT refinement segment: the HT SigProp bits from its first byte, the HT MagRef bits from its last one
        var significancePropagationBytes = passes > 1 ? WriteForward(significancePropagation) : [];
        var magnitudeRefinementBytes = passes == 3 ? WriteBackward(magnitudeRefinement) : [];

        // A segment does not end with FF, and FF is not followed by a byte above 8F
        if (significancePropagationBytes.Count > 0 && significancePropagationBytes[^1] == 0xFF)
            significancePropagationBytes.Add(0);

        byte[] refinement = [.. significancePropagationBytes, .. magnitudeRefinementBytes];

        // T.814 B.3: the bit-planes the packet header gives as empty, the placeholder passes and the HT sets before this
        // one are skipped. The HT cleanup pass is one bit-plane higher when refinement passes follow it
        var skipped = zeroBitPlanes - (passes > 1 ? 1 : 0);
        var headerZeroBitPlanes = skipped - placeholderSets;
        Py.Assert(headerZeroBitPlanes >= 0, "zero bit-planes");
        var cleanupPasses = (3 * placeholderSets) + 1;
        List<List<(int Passes, byte[] Data)>> contributions = packets switch
        {
            AuthoredPackets.OnePacket => [[(cleanupPasses, cleanup), .. passes > 1 ? [(passes - 1, refinement)] : Array.Empty<(int, byte[])>()]],
            AuthoredPackets.RefinementInSecondPacket => [[(cleanupPasses, cleanup)], [(passes - 1, refinement)]],
            AuthoredPackets.OnePassPerPacket => [[(cleanupPasses, cleanup)], [(1, [.. significancePropagationBytes])], [(1, [.. magnitudeRefinementBytes])]],
            AuthoredPackets.SecondSetInSecondPacket => [[(cleanupPasses, cleanup)], [(2, refinement), (1, [])]],
            _ => [[(3, [])], [(1, cleanup)]],
        };
        Py.Assert(packets is AuthoredPackets.OnePacket || passes > 1 || packets == AuthoredPackets.PlaceholdersInFirstPacket, "passes for the other packets");
        Py.Assert(packets is not (AuthoredPackets.OnePassPerPacket or AuthoredPackets.SecondSetInSecondPacket) || passes == 3, "three passes");
        Py.Assert(packets != AuthoredPackets.PlaceholdersInFirstPacket || (passes == 1 && placeholderSets == 1), "three placeholder passes");

        // T.800 B.10: the packet is not empty; the code-block is included (a tag tree of one node, then one bit once it
        // was included); its zero bit-planes (a tag tree of one node); the number of coding passes; the increment of
        // Lblock; the length of each codeword segment, on Lblock bits plus the base-2 logarithm of its number of passes
        var tile = new List<byte>();
        var blockLengthBits = 3;
        for (var index = 0; index < contributions.Count; index++)
        {
            var writer = new PacketBitWriter();
            writer.WriteBit(1);
            writer.WriteBit(1);
            if (index == 0)
            {
                for (var i = 0; i < headerZeroBitPlanes; i++)
                    writer.WriteBit(0);

                writer.WriteBit(1);
            }

            WritePassCount(writer, contributions[index].Sum(contribution => contribution.Passes));
            var needed = Math.Max(blockLengthBits, contributions[index].Max(contribution => BitLength(contribution.Data.Length) - Log2(contribution.Passes)));
            for (var i = blockLengthBits; i < needed; i++)
                writer.WriteBit(1);

            writer.WriteBit(0);
            blockLengthBits = needed;
            foreach (var (count, bytes) in contributions[index])
                writer.WriteBits(bytes.Length, blockLengthBits + Log2(count));

            tile.AddRange(writer.Finish());
            foreach (var (_, bytes) in contributions[index])
                tile.AddRange(bytes);
        }

        // The main header of ojph_compress, with the number of layers and the vertically causal bit; the SOT marker segment
        // with the new length of the tile-part
        var main = data.AsSpan(0, tilePart.Offset).ToArray();
        var cod = FindMarkerSegment(main, 0x52);
        BinaryPrimitives.WriteUInt16BigEndian(main.AsSpan(cod + 6), (ushort)contributions.Count);
        Py.Assert(main[cod + 12] == 0x40, "the code-block style of ojph_compress");
        if (isVerticallyCausal)
            main[cod + 12] = 0x48;

        var start = data.AsSpan(tilePart.Offset, 14).ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(start.AsSpan(6), (uint)(14 + tile.Count));
        byte[] codestream = [.. main, .. start, .. tile, 0xFF, 0xD9];

        // T.814 7.6: with an HT SigProp pass and no HT MagRef pass, the last bit of the coefficients the HT cleanup pass
        // coded is not in the codestream. The decoders show the middle of the two values it can have: the odd one
        var shown = coded.Select(value => passes == 2 && Math.Abs(value) >= 2 ? ((Math.Abs(value) & ~1) + 1) * Math.Sign(value) : value).ToArray();
        var frame = new Img(width, height, "gray", 8, shown.Select(value => new Px(128 + value)));
        var layout = packets switch
        {
            AuthoredPackets.OnePacket => "one packet",
            AuthoredPackets.RefinementInSecondPacket => "two layers: the HT cleanup pass, then the HT refinement passes",
            AuthoredPackets.OnePassPerPacket => "three layers: one coding pass in each packet",
            AuthoredPackets.SecondSetInSecondPacket => "two layers: the HT cleanup pass, then the HT refinement passes and the HT cleanup pass of a second HT set, whose segment is empty",
            _ => "two layers: three placeholder passes, then the HT cleanup pass",
        };
        var description = $"{{output}}.j2c: its HT cleanup segment ({CommonCorpus.Str(cleanup.Length)} bytes) with a hand-written packet header: {CommonCorpus.Str(3 * placeholderSets)} placeholder passes, {CommonCorpus.Str(passes)} coding passes, {CommonCorpus.Str(headerZeroBitPlanes)} zero bit-planes"
            + (passes > 1 ? $", an HT refinement segment of {CommonCorpus.Str(refinement.Length)} bytes ({CommonCorpus.Str(significancePropagation.Count)} HT SigProp bits{(passes == 3 ? $", {CommonCorpus.Str(magnitudeRefinement.Count)} HT MagRef bits" : "")})" : "")
            + (isVerticallyCausal ? ", vertically causal contexts" : "")
            + "; " + layout;
        return new Authored(new Encoded(codestream, [.. written.Commands, description]), coded, frame, significancePropagation.Count, magnitudeRefinement.Count);
    }

    /// <summary>
    /// The bits of the HT SigProp and HT MagRef passes for coefficients whose halves the HT cleanup pass codes (T.814 7.4
    /// and 7.5): a sample of magnitude 2 or more is significant after the HT cleanup pass; a sample of magnitude 0 or 1 gets
    /// a bit in the HT SigProp pass when a neighbor is significant, or was given the bit 1 earlier in the scan.
    /// </summary>
    private static (int[] Coded, List<int> SignificancePropagation, List<int> MagnitudeRefinement) PlanRefinement(List<int> coefficients, int width, int height, bool isVerticallyCausal)
    {
        var coded = coefficients.ToArray();
        var significant = coded.Select(value => Math.Abs(value) >= 2).ToArray();
        var refined = new bool[coded.Length];
        var significancePropagation = new List<int>();
        var magnitudeRefinement = new List<int>();

        // Stripes of four rows; in a stripe, groups of four columns: the magnitude bits of a group, then its sign bits
        for (var stripe = 0; stripe < height; stripe += 4)
        {
            var stripeEnd = Math.Min(stripe + 4, height);
            for (var group = 0; group < width; group += 4)
            {
                var samples = new List<(int X, int Y)>();
                for (var x = group; x < Math.Min(group + 4, width); x++)
                {
                    for (var y = stripe; y < stripeEnd; y++)
                        samples.Add((x, y));
                }

                foreach (var (x, y) in samples)
                {
                    var n = (y * width) + x;
                    if (significant[n])
                        continue;

                    var hasNeighbor = false;
                    for (var neighborY = y - 1; neighborY <= y + 1; neighborY++)
                    {
                        for (var neighborX = x - 1; neighborX <= x + 1; neighborX++)
                        {
                            if ((neighborX == x && neighborY == y) || neighborX < 0 || neighborX >= width || neighborY < 0 || neighborY >= height)
                                continue;

                            // The vertically causal contexts ignore the next stripe
                            if (isVerticallyCausal && neighborY >= stripeEnd)
                                continue;

                            var m = (neighborY * width) + neighborX;
                            var isEarlier = neighborY < stripe || (neighborY < stripeEnd && (neighborX < x || (neighborX == x && neighborY < y)));
                            hasNeighbor |= significant[m] || (isEarlier && refined[m]);
                        }
                    }

                    if (hasNeighbor)
                    {
                        refined[n] = Math.Abs(coded[n]) == 1;
                        significancePropagation.Add(refined[n] ? 1 : 0);
                    }
                    else
                    {
                        // The pass cannot give a bit to the sample
                        coded[n] = 0;
                    }
                }

                foreach (var (x, y) in samples)
                {
                    var n = (y * width) + x;
                    if (!significant[n] && refined[n])
                        significancePropagation.Add(coded[n] < 0 ? 1 : 0);
                }
            }
        }

        for (var stripe = 0; stripe < height; stripe += 4)
        {
            for (var x = 0; x < width; x++)
            {
                for (var y = stripe; y < Math.Min(stripe + 4, height); y++)
                {
                    var n = (y * width) + x;
                    if (significant[n])
                        magnitudeRefinement.Add(Math.Abs(coded[n]) & 1);
                }
            }
        }

        return (coded, significancePropagation, magnitudeRefinement);
    }

    /// <summary>T.814 7.1.5: the first bit in the least significant bit of the first byte; seven bits in the byte that follows FF.</summary>
    private static List<byte> WriteForward(IReadOnlyList<int> bits)
    {
        var bytes = new List<byte>();
        var (current, count, capacity) = (0, 0, 8);
        foreach (var bit in bits)
        {
            current |= bit << count;
            count++;
            if (count == capacity)
            {
                bytes.Add((byte)current);
                capacity = current == 0xFF ? 7 : 8;
                (current, count) = (0, 0);
            }
        }

        if (count > 0)
            bytes.Add((byte)current);
        return bytes;
    }

    /// <summary>
    /// T.814 7.1.6: the bytes are read from the last one; the first bit in the least significant bit; seven bits in a byte
    /// whose seven low bits are 1 when the byte read before it is above 8F (FF before any byte).
    /// </summary>
    private static List<byte> WriteBackward(IReadOnlyList<int> bits)
    {
        var bytes = new List<byte>();
        var (current, count, last) = (0, 0, 0xFF);
        foreach (var bit in bits)
        {
            current |= bit << count;
            count++;
            if (count == 8 || (count == 7 && last > 0x8F && current == 0x7F))
            {
                bytes.Add((byte)current);
                last = current;
                (current, count) = (0, 0);
            }
        }

        if (count > 0)
            bytes.Add((byte)current);
        bytes.Reverse();
        return bytes;
    }

    /// <summary>T.800 Table B.4: the codeword of a number of coding passes.</summary>
    private static void WritePassCount(PacketBitWriter writer, int count)
    {
        if (count == 1)
        {
            writer.WriteBits(0, 1);
        }
        else if (count == 2)
        {
            writer.WriteBits(0b10, 2);
        }
        else if (count <= 5)
        {
            writer.WriteBits(0b11, 2);
            writer.WriteBits(count - 3, 2);
        }
        else if (count <= 36)
        {
            writer.WriteBits(0b1111, 4);
            writer.WriteBits(count - 6, 5);
        }
        else
        {
            writer.WriteBits(0b1_1111_1111, 9);
            writer.WriteBits(count - 37, 7);
        }
    }

    private static int BitLength(int value) => value == 0 ? 0 : Log2(value) + 1;

    private static int Log2(int value) => System.Numerics.BitOperations.Log2((uint)value);

    /// <summary>The offset of the first marker segment of a type in a main header.</summary>
    private static int FindMarkerSegment(ReadOnlySpan<byte> header, int marker)
    {
        var position = 2;
        while (position + 4 <= header.Length)
        {
            Py.Assert(header[position] == 0xFF, "a marker");
            if (header[position + 1] == marker)
                return position;

            position += 2 + BinaryPrimitives.ReadUInt16BigEndian(header[(position + 2)..]);
        }

        throw new ToolException($"The main header has no marker segment FF{marker:X2}.");
    }

    /// <summary>The bits of a packet header (T.800 B.10.1): the most significant bit first; seven bits in the byte that follows FF.</summary>
    private sealed class PacketBitReader(byte[] data, int position)
    {
        private int _position = position;
        private int _bits;
        private int _current;

        public int ReadBit()
        {
            if (_bits == 0)
            {
                var previous = _current;
                _current = data[_position++];
                _bits = previous == 0xFF ? 7 : 8;
            }

            _bits--;
            return (_current >> _bits) & 1;
        }

        public int ReadBits(int count)
        {
            var value = 0;
            for (var i = 0; i < count; i++)
                value = (value << 1) | ReadBit();
            return value;
        }

        /// <summary>The offset of the byte that follows the header: a header does not end with FF.</summary>
        public int Align()
        {
            if (_current == 0xFF && _bits == 0)
                _position++;
            _bits = 0;
            return _position;
        }
    }

    private sealed class PacketBitWriter
    {
        private readonly List<byte> _bytes = [];
        private int _current;
        private int _count;
        private int _capacity = 8;

        public void WriteBit(int bit)
        {
            _current = (_current << 1) | bit;
            _count++;
            if (_count == _capacity)
            {
                _bytes.Add((byte)_current);
                _capacity = _current == 0xFF ? 7 : 8;
                (_current, _count) = (0, 0);
            }
        }

        public void WriteBits(int value, int count)
        {
            for (var i = count - 1; i >= 0; i--)
                WriteBit((value >> i) & 1);
        }

        public List<byte> Finish()
        {
            while (_count != 0)
                WriteBit(0);

            if (_bytes.Count > 0 && _bytes[^1] == 0xFF)
                _bytes.Add(0);
            return _bytes;
        }
    }
}
