using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.Imaging.TestHarness.Dpx;

/// <summary>
/// Assembles Kodak Cineon files (Image File Format Proposal for Digital Pictures, draft 4.5) from the samples of their
/// channels, pixel interleaved, for the tests and the fixtures and for malformed files. It is written from the draft and
/// from what ImageMagick and OpenImageIO read (the packing rules are those of <see cref="PackLine"/>), and never uses the
/// library under test. It writes what it is told to: nothing is validated.
/// </summary>
public sealed class CineonFileBuilder
{
    /// <summary>Gets or sets the number of pixels per line of every channel.</summary>
    public int Width { get; set; }

    /// <summary>Gets or sets the number of lines of every channel.</summary>
    public int Height { get; set; }

    /// <summary>Gets or sets a value indicating whether the file is big-endian.</summary>
    public bool BigEndian { get; set; } = true;

    /// <summary>Gets or sets the designators of the channels (byte 0 in the high byte, byte 1 in the low byte).</summary>
    public IReadOnlyList<ushort> Designators { get; set; } = [1, 2, 3];

    /// <summary>Gets or sets the number of bits of each sample.</summary>
    public int BitsPerSample { get; set; } = 10;

    /// <summary>Gets or sets the bits of a channel other than the first one, to write channels of different depths.</summary>
    public int? OtherChannelBits { get; set; }

    /// <summary>Gets or sets the width of the channels other than the first one, to write channels of different sizes.</summary>
    public int? OtherChannelWidth { get; set; }

    /// <summary>Gets or sets the packing field (1.29).</summary>
    public byte Packing { get; set; } = 5;

    /// <summary>Gets or sets the data interleave (1.28).</summary>
    public byte Interleave { get; set; }

    /// <summary>Gets or sets the data sign (1.30).</summary>
    public byte Sign { get; set; }

    /// <summary>Gets or sets the image sense (1.31).</summary>
    public byte Sense { get; set; }

    /// <summary>Gets or sets the orientation code (1.12).</summary>
    public byte Orientation { get; set; }

    /// <summary>Gets or sets the number of channels written in field 1.13, or <see langword="null"/> for the number of designators.</summary>
    public int? ChannelCount { get; set; }

    /// <summary>Gets or sets the end-of-line padding (1.32), in bytes.</summary>
    public uint EndOfLinePadding { get; set; }

    /// <summary>Gets or sets the offset of the image data: 1,024 omits the motion-picture section, 2,048 or more writes it.</summary>
    public int DataOffset { get; set; } = 2048;

    /// <summary>Gets or sets the version of the header format.</summary>
    public string Version { get; set; } = "V4.5";

    /// <summary>Gets the text fields to write, by offset and length.</summary>
    public IDictionary<(int Offset, int Length), string> Text { get; } = new Dictionary<(int Offset, int Length), string>();

    /// <summary>Gets the 32-bit fields to write, by offset.</summary>
    public IDictionary<int, uint> Words { get; } = new Dictionary<int, uint>();

    /// <summary>Gets the 8-bit fields to write, by offset.</summary>
    public IDictionary<int, byte> ByteFields { get; } = new Dictionary<int, byte>();

    /// <summary>Gets or sets the samples, pixel interleaved in the order of the channels: <c>Designators.Count</c> × <see cref="Width"/> per line.</summary>
    public IReadOnlyList<uint> Samples { get; set; } = [];

    /// <summary>Gets or sets bytes that replace the image data, or <see langword="null"/> to pack the samples.</summary>
    public ReadOnlyMemory<byte>? RawData { get; set; }

    /// <summary>Assembles the file.</summary>
    public byte[] Build()
    {
        var channels = Designators.Count;
        var header = new byte[DataOffset];
        header.AsSpan(0, Math.Min(DataOffset, 2048)).Fill(0xFF);
        foreach (var (offset, length) in TextFields)
        {
            if (offset + length <= header.Length)
            {
                header.AsSpan(offset, length).Clear();
            }
        }

        using var data = new MemoryStream();
        if (RawData is not null)
        {
            data.Write(RawData.Value.Span);
        }
        else
        {
            for (var y = 0; y < Height; y++)
            {
                data.Write(PackLine(Samples.Skip(y * Width * channels).Take(Width * channels).ToArray(), channels, BitsPerSample, Packing, BigEndian));
                if (y < Height - 1)
                {
                    data.Write(new byte[EndOfLinePadding]);
                }
            }
        }

        var imageData = data.ToArray();
        var span = header.AsSpan();
        U32(span, 0, 0x802A5FD7);
        U32(span, 4, (uint)DataOffset);
        U32(span, 8, 1024);
        U32(span, 12, DataOffset >= 2048 ? 1024u : 0u);
        U32(span, 16, DataOffset > 2048 ? (uint)(DataOffset - 2048) : 0u);
        U32(span, 20, (uint)(header.Length + imageData.Length));
        Encoding.ASCII.GetBytes(Version).CopyTo(span[24..]);
        span[192] = Orientation;
        span[193] = (byte)(ChannelCount ?? channels);
        span[194] = 0;
        span[195] = 0;
        for (var c = 0; c < Math.Min(channels, 8); c++)
        {
            var offset = 196 + (c * 28);
            span[offset] = (byte)(Designators[c] >> 8);
            span[offset + 1] = (byte)Designators[c];
            span[offset + 2] = (byte)(c > 0 && OtherChannelBits is { } bits ? bits : BitsPerSample);
            span[offset + 3] = 0;
            U32(span, offset + 4, (uint)(c > 0 && OtherChannelWidth is { } width ? width : Width));
            U32(span, offset + 8, (uint)Height);
            R32(span, offset + 12, 0);
            R32(span, offset + 16, 0);
            R32(span, offset + 20, (1 << BitsPerSample) - 1);
            R32(span, offset + 24, 2.048f);
        }

        float[] chromaticities = [0.3127f, 0.329f, 0.64f, 0.33f, 0.3f, 0.6f, 0.15f, 0.06f];
        for (var i = 0; i < 8; i++)
        {
            R32(span, 420 + (i * 4), chromaticities[i]);
        }

        span[680] = Interleave;
        span[681] = Packing;
        span[682] = Sign;
        span[683] = Sense;
        U32(span, 684, EndOfLinePadding);
        U32(span, 688, 0);
        foreach (var ((offset, length), value) in Text)
        {
            var field = span.Slice(offset, length);
            field.Clear();
            Encoding.Latin1.GetBytes(value[..Math.Min(value.Length, length)]).CopyTo(field);
        }

        foreach (var (offset, value) in Words)
        {
            U32(span, offset, value);
        }

        foreach (var (offset, value) in ByteFields)
        {
            span[offset] = value;
        }

        return [.. header, .. imageData];

        void U32(Span<byte> destination, int offset, uint value)
        {
            if (offset + 4 > destination.Length)
                return;

            if (BigEndian)
            {
                BinaryPrimitives.WriteUInt32BigEndian(destination[offset..], value);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(destination[offset..], value);
            }
        }

        void R32(Span<byte> destination, int offset, float value) => U32(destination, offset, BitConverter.SingleToUInt32Bits(value));
    }

    private static readonly (int Offset, int Length)[] TextFields =
    [
        (24, 8), (32, 100), (132, 12), (144, 12), (452, 200), (720, 100), (820, 12), (832, 12), (844, 64), (908, 32), (940, 32), (1036, 32), (1076, 32), (1108, 200),
    ];

    /// <summary>
    /// Packs the samples of a line: 8 and 16-bit samples as a stream of bytes and of 16-bit words, the line padded to a
    /// whole cell (as ImageMagick writes them); other depths in cells of 8, 16 or 32 bits (field 1.29), left or right justified, one pixel per cell unless the
    /// high bit of the packing is set or the image has one channel, every line starting on a new cell.
    /// </summary>
    public static byte[] PackLine(IReadOnlyList<uint> samples, int channels, int bits, byte packing, bool bigEndian)
    {
        if (bits is 8 or 16)
        {
            // The line ends on a cell boundary (8, 16 or 32 bits)
            var cellBytes = (packing & 0x7F) switch
            {
                3 or 4 => 2,
                5 or 6 => 4,
                _ => 1,
            };
            var stream = new byte[((samples.Count * (bits / 8)) + cellBytes - 1) / cellBytes * cellBytes];
            for (var i = 0; i < samples.Count; i++)
            {
                if (bits == 8)
                {
                    stream[i] = (byte)samples[i];
                }
                else if (bigEndian)
                {
                    BinaryPrimitives.WriteUInt16BigEndian(stream.AsSpan(i * 2), (ushort)samples[i]);
                }
                else
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(stream.AsSpan(i * 2), (ushort)samples[i]);
                }
            }

            return stream;
        }

        var code = packing & 0x7F;
        var cellBits = code switch
        {
            1 or 2 => 8,
            3 or 4 => 16,
            5 or 6 => 32,
            _ => throw new ArgumentOutOfRangeException(nameof(packing)),
        };
        var left = (code & 1) == 1;
        var many = channels == 1 || (packing & 0x80) != 0;
        var perCell = cellBits / bits;
        if (!many)
        {
            perCell = Math.Min(perCell, channels);
        }

        var cells = new List<uint>();
        void AddCell(IReadOnlyList<uint> fields)
        {
            uint value = 0;
            for (var j = 0; j < fields.Count; j++)
            {
                var shift = left ? cellBits - (bits * (j + 1)) : bits * (fields.Count - 1 - j);
                value |= fields[j] << shift;
            }

            cells.Add(value);
        }

        if (many)
        {
            for (var i = 0; i < samples.Count; i += perCell)
            {
                AddCell(samples.Skip(i).Take(perCell).ToArray());
            }
        }
        else
        {
            for (var p = 0; p < samples.Count / channels; p++)
            {
                for (var k = 0; k < channels; k += perCell)
                {
                    AddCell(samples.Skip((p * channels) + k).Take(Math.Min(perCell, channels - k)).ToArray());
                }
            }
        }

        var bytes = cellBits / 8;
        var line = new byte[cells.Count * bytes];
        for (var i = 0; i < cells.Count; i++)
        {
            switch (bytes)
            {
                case 1:
                    line[i] = (byte)cells[i];
                    break;
                case 2 when bigEndian:
                    BinaryPrimitives.WriteUInt16BigEndian(line.AsSpan(i * 2), (ushort)cells[i]);
                    break;
                case 2:
                    BinaryPrimitives.WriteUInt16LittleEndian(line.AsSpan(i * 2), (ushort)cells[i]);
                    break;
                default:
                    if (bigEndian)
                    {
                        BinaryPrimitives.WriteUInt32BigEndian(line.AsSpan(i * 4), cells[i]);
                    }
                    else
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(line.AsSpan(i * 4), cells[i]);
                    }

                    break;
            }
        }

        return line;
    }
}
