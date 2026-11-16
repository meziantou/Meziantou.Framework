using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.Imaging.TestHarness.Dpx;

/// <summary>
/// Assembles DPX files (SMPTE ST 268:2014) from the components of their first image element, for the tests and the fixtures
/// that need files the encoders do not write (every descriptor, bit depth, packing method and orientation, both byte
/// orders, the industry-specific headers) and for malformed files. It is written from the standard and from what FFmpeg,
/// ImageMagick and OpenImageIO read (the packing rules are those of <see cref="PackLine"/>), and never uses the library under
/// test. It writes what it is told to: nothing is validated.
/// </summary>
public sealed class DpxFileBuilder
{
    /// <summary>Gets or sets the number of pixels per line.</summary>
    public int Width { get; set; }

    /// <summary>Gets or sets the number of lines.</summary>
    public int Height { get; set; }

    /// <summary>Gets or sets a value indicating whether the file is big-endian (<c>SDPX</c>).</summary>
    public bool BigEndian { get; set; } = true;

    /// <summary>Gets or sets the version of the header format.</summary>
    public string Version { get; set; } = "V2.0";

    /// <summary>Gets or sets the descriptor of the first image element (Table 1).</summary>
    public int Descriptor { get; set; } = 50;

    /// <summary>Gets or sets the bit depth of the components (Table 3A).</summary>
    public int BitDepth { get; set; } = 8;

    /// <summary>Gets or sets the packing method (Table 3B).</summary>
    public int Packing { get; set; }

    /// <summary>Gets or sets the encoding (Table 3C).</summary>
    public int DataEncoding { get; set; }

    /// <summary>Gets or sets the data sign (field 21.1).</summary>
    public uint DataSign { get; set; }

    /// <summary>Gets or sets the transfer characteristic (Table 5A).</summary>
    public int Transfer { get; set; } = 255;

    /// <summary>Gets or sets the colorimetric specification (Table 5B).</summary>
    public int Colorimetric { get; set; } = 255;

    /// <summary>Gets or sets the orientation code (Table 2).</summary>
    public int Orientation { get; set; }

    /// <summary>Gets or sets the number of image elements (field 18); only the first one has a structure.</summary>
    public int ElementCount { get; set; } = 1;

    /// <summary>Gets or sets the reference low data code value, or <see langword="null"/> for undefined.</summary>
    public uint? ReferenceLowCode { get; set; }

    /// <summary>Gets or sets the reference high data code value, or <see langword="null"/> for undefined.</summary>
    public uint? ReferenceHighCode { get; set; }

    /// <summary>Gets or sets the reference low quantity, or <see langword="null"/> for undefined.</summary>
    public float? ReferenceLowQuantity { get; set; }

    /// <summary>Gets or sets the reference high quantity, or <see langword="null"/> for undefined.</summary>
    public float? ReferenceHighQuantity { get; set; }

    /// <summary>Gets or sets the encryption key (field 15).</summary>
    public uint EncryptionKey { get; set; } = uint.MaxValue;

    /// <summary>Gets or sets the offset of the image data: 1,664 omits the industry-specific headers, 2,048 or more writes them.</summary>
    public int DataOffset { get; set; } = 2048;

    /// <summary>Gets or sets the offset written in field 21.12, or <see langword="null"/> for <see cref="DataOffset"/>.</summary>
    public uint? ElementDataOffset { get; set; }

    /// <summary>Gets or sets the end-of-line padding (field 21.13), in bytes; <see langword="null"/> writes the undefined value and no padding.</summary>
    public uint? EndOfLinePadding { get; set; } = 0;

    /// <summary>Gets or sets the pixel aspect ratio (field 41), or <see langword="null"/> for undefined.</summary>
    public (uint Horizontal, uint Vertical)? PixelAspectRatio { get; set; }

    /// <summary>Gets the text fields to write, by offset and length (field 9 at 36, 100; field 12 at 160, 100, ...).</summary>
    public IDictionary<(int Offset, int Length), string> Text { get; } = new Dictionary<(int Offset, int Length), string>();

    /// <summary>Gets the 32-bit fields to write, by offset (such as the frame position at 1712 or the time code at 1920).</summary>
    public IDictionary<int, uint> Words { get; } = new Dictionary<int, uint>();

    /// <summary>Gets the 8-bit fields to write, by offset (such as the interlace at 1928).</summary>
    public IDictionary<int, byte> ByteFields { get; } = new Dictionary<int, byte>();

    /// <summary>Gets or sets the integer components of the lines, in the order of the descriptor: <see cref="ComponentsPerPixel"/> × <see cref="Width"/> per line.</summary>
    public IReadOnlyList<uint> Components { get; set; } = [];

    /// <summary>Gets or sets the floating-point components of 32 and 64-bit elements, in the order of the descriptor.</summary>
    public IReadOnlyList<double> FloatComponents { get; set; } = [];

    /// <summary>Gets or sets bytes written after the image data.</summary>
    public ReadOnlyMemory<byte> Trailer { get; set; }

    /// <summary>Gets or sets bytes that replace the image data, or <see langword="null"/> to pack the components.</summary>
    public ReadOnlyMemory<byte>? RawData { get; set; }

    /// <summary>Gets the number of components of a pixel of the descriptor (a pixel of a 4:2:2 element is one picture element).</summary>
    public int ComponentsPerPixel => GetComponentsPerPixel(Descriptor);

    /// <summary>Gets the number of components of a pixel of a descriptor (Table 1).</summary>
    public static int GetComponentsPerPixel(int descriptor) => descriptor switch
    {
        100 => 2,
        50 or 101 or 102 => 3,
        51 or 52 or 103 => 4,
        _ => 1,
    };

    /// <summary>Gets whether a descriptor has more than one component.</summary>
    public static bool IsMultiComponent(int descriptor) => descriptor is >= 50;

    /// <summary>Assembles the file.</summary>
    public byte[] Build()
    {
        var header = new byte[DataOffset];
        header.AsSpan(0, Math.Min(DataOffset, 2048)).Fill(0xFF);
        foreach (var (offset, length) in TextFields)
        {
            if (offset + length <= header.Length)
            {
                header.AsSpan(offset, length).Clear();
            }
        }

        if (header.Length > 1931)
        {
            header[1931] = 0;
        }

        var cpp = ComponentsPerPixel;
        var lines = new List<byte[]>();
        if (RawData is null)
        {
            for (var y = 0; y < Height; y++)
            {
                lines.Add(BitDepth >= 32
                    ? PackFloatLine(FloatComponents.Skip(y * Width * cpp).Take(Width * cpp).ToArray(), BitDepth, BigEndian)
                    : PackLine(Components.Skip(y * Width * cpp).Take(Width * cpp).ToArray(), BitDepth, Packing, IsMultiComponent(Descriptor), BigEndian));
            }
        }

        var padding = (int)(EndOfLinePadding ?? 0);
        using var data = new MemoryStream();
        for (var y = 0; y < lines.Count; y++)
        {
            data.Write(lines[y]);
            if (y < lines.Count - 1)
            {
                data.Write(new byte[padding]);
            }
        }

        var imageData = RawData is { } raw ? raw.ToArray() : data.ToArray();
        var fileSize = (uint)(header.Length + imageData.Length + Trailer.Length);
        var span = header.AsSpan();
        Encoding.ASCII.GetBytes(BigEndian ? "SDPX" : "XPDS").CopyTo(span);
        U32(span, 4, (uint)DataOffset);
        span.Slice(8, 8).Clear();
        Encoding.ASCII.GetBytes(Version).CopyTo(span[8..]);
        U32(span, 16, fileSize);
        U32(span, 20, 1);
        U32(span, 24, 1664);
        U32(span, 28, DataOffset >= 2048 ? 384u : 0u);
        U32(span, 32, DataOffset > 2048 ? (uint)(DataOffset - 2048) : 0u);
        U32(span, 660, EncryptionKey);
        U16(span, 768, (ushort)Orientation);
        U16(span, 770, (ushort)ElementCount);
        U32(span, 772, (uint)Width);
        U32(span, 776, (uint)Height);
        const int Element = 780;
        U32(span, Element, DataSign);
        U32(span, Element + 4, ReferenceLowCode ?? uint.MaxValue);
        U32(span, Element + 8, ReferenceLowQuantity is { } lowQuantity ? BitConverter.SingleToUInt32Bits(lowQuantity) : uint.MaxValue);
        U32(span, Element + 12, ReferenceHighCode ?? uint.MaxValue);
        U32(span, Element + 16, ReferenceHighQuantity is { } highQuantity ? BitConverter.SingleToUInt32Bits(highQuantity) : uint.MaxValue);
        span[Element + 20] = (byte)Descriptor;
        span[Element + 21] = (byte)Transfer;
        span[Element + 22] = (byte)Colorimetric;
        span[Element + 23] = (byte)BitDepth;
        U16(span, Element + 24, (ushort)Packing);
        U16(span, Element + 26, (ushort)DataEncoding);
        U32(span, Element + 28, ElementDataOffset ?? (uint)DataOffset);
        U32(span, Element + 32, EndOfLinePadding ?? uint.MaxValue);
        U32(span, Element + 36, 0);
        U32(span, 1628, PixelAspectRatio?.Horizontal ?? uint.MaxValue);
        U32(span, 1632, PixelAspectRatio?.Vertical ?? uint.MaxValue);
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

        return [.. header, .. imageData, .. Trailer.Span];

        void U16(Span<byte> destination, int offset, ushort value)
        {
            if (offset + 2 > destination.Length)
                return;

            if (BigEndian)
            {
                BinaryPrimitives.WriteUInt16BigEndian(destination[offset..], value);
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], value);
            }
        }

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
    }

    /// <summary>The text fields of the headers (offset, length): their undefined value is NUL, not all bits set.</summary>
    private static readonly (int Offset, int Length)[] TextFields =
    [
        (8, 8), (36, 100), (136, 24), (160, 100), (260, 200), (460, 200), (664, 104), (820, 32), (1356, 52), (1432, 100), (1532, 24),
        (1556, 32), (1588, 32), (1644, 20), (1664, 48), (1732, 32), (1764, 100), (1864, 56), (1972, 76),
    ];

    /// <summary>
    /// Packs the integer components of a line into 32-bit words of the byte order of the file, the line padded to a whole
    /// word: 1 bit from the least significant bit of each word; 8 bits as bytes; 16 bits as 16-bit words; 12 bits filled as
    /// 16-bit words (method A: the sample above four padding bits, method B: below them); 10 bits filled as three per word
    /// (method A: above two padding bits, method B: below them; the first component at the top of a multi-component element
    /// and at the bottom of a single-component one); 10 and 12 bits packed from the least significant bit of each word.
    /// </summary>
    public static byte[] PackLine(IReadOnlyList<uint> components, int bitDepth, int packing, bool multiComponent, bool bigEndian)
    {
        var n = components.Count;
        long words = bitDepth switch
        {
            1 => (n + 31) / 32,
            8 => (n + 3) / 4,
            10 when packing == 0 => ((n * 10L) + 31) / 32,
            10 => (n + 2) / 3,
            12 when packing == 0 => ((n * 12L) + 31) / 32,
            12 or 16 => (n + 1) / 2,
            _ => throw new ArgumentOutOfRangeException(nameof(bitDepth)),
        };
        var line = new byte[words * 4];
        switch (bitDepth)
        {
            case 8:
                for (var i = 0; i < n; i++)
                {
                    line[i] = (byte)components[i];
                }

                return line;

            case 12 when packing != 0:
            case 16:
                for (var i = 0; i < n; i++)
                {
                    var value = bitDepth == 16 ? components[i] : packing == 1 ? components[i] << 4 : components[i];
                    if (bigEndian)
                    {
                        BinaryPrimitives.WriteUInt16BigEndian(line.AsSpan(i * 2), (ushort)value);
                    }
                    else
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(line.AsSpan(i * 2), (ushort)value);
                    }
                }

                return line;

            case 10 when packing != 0:
            {
                var wordValues = new uint[words];
                for (var i = 0; i < n; i++)
                {
                    var j = i % 3;
                    var shift = packing == 1
                        ? (multiComponent ? 22 - (10 * j) : 2 + (10 * j))
                        : (multiComponent ? 20 - (10 * j) : 10 * j);
                    wordValues[i / 3] |= components[i] << shift;
                }

                WriteWords(line, wordValues, bigEndian);
                return line;
            }

            default:
            {
                // A bit stream from the least significant bit of each word: 1-bit components and packed 10 and 12-bit ones
                var wordValues = new uint[words];
                for (var i = 0; i < n; i++)
                {
                    var offset = (long)i * bitDepth;
                    var value = (ulong)components[i] << (int)(offset & 31);
                    wordValues[offset >> 5] |= (uint)value;
                    if ((offset & 31) + bitDepth > 32)
                    {
                        wordValues[(offset >> 5) + 1] |= (uint)(value >> 32);
                    }
                }

                WriteWords(line, wordValues, bigEndian);
                return line;
            }
        }
    }

    /// <summary>Writes the floating-point components of a line: IEEE single or double precision numbers in the byte order of the file.</summary>
    public static byte[] PackFloatLine(IReadOnlyList<double> components, int bitDepth, bool bigEndian)
    {
        var size = bitDepth / 8;
        var line = new byte[components.Count * size];
        for (var i = 0; i < components.Count; i++)
        {
            if (bitDepth == 32)
            {
                var bits = BitConverter.SingleToUInt32Bits((float)components[i]);
                if (bigEndian)
                {
                    BinaryPrimitives.WriteUInt32BigEndian(line.AsSpan(i * 4), bits);
                }
                else
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(line.AsSpan(i * 4), bits);
                }
            }
            else
            {
                var bits = BitConverter.DoubleToUInt64Bits(components[i]);
                if (bigEndian)
                {
                    BinaryPrimitives.WriteUInt64BigEndian(line.AsSpan(i * 8), bits);
                }
                else
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(line.AsSpan(i * 8), bits);
                }
            }
        }

        return line;
    }

    private static void WriteWords(byte[] line, uint[] words, bool bigEndian)
    {
        for (var i = 0; i < words.Length; i++)
        {
            if (bigEndian)
            {
                BinaryPrimitives.WriteUInt32BigEndian(line.AsSpan(i * 4), words[i]);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(line.AsSpan(i * 4), words[i]);
            }
        }
    }
}
