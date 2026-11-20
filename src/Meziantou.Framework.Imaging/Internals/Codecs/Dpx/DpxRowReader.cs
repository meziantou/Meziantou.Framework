using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the components of a stored line of a DPX image element (SMPTE ST 268, Tables 3A and 3B, Annex C) and writes the
/// pixels of the line in the source layout of the image.
/// </summary>
/// <remarks>
/// <para>
/// A line is a stream of components in the order of the descriptor, packed into 32-bit words stored in the byte order of the
/// file; every line starts on a 32-bit word (Table 3C: "packing will always break to the next 32-bit word at scan line
/// boundaries"). How each depth sits in the words, as FFmpeg, ImageMagick and OpenImageIO read it:
/// </para>
/// <list type="bullet">
/// <item><description>1 bit: a bit stream from the least significant bit of each word (Table 3B, note 1).</description></item>
/// <item><description>8 bits: a byte stream; 16 bits: a stream of 16-bit words in the byte order of the file.</description></item>
/// <item><description>
/// 10 bits, filled (method A): three components per word, above two padding bits; method B: below two padding bits. The
/// first component of a word of a multi-component element is in the most significant bits; for a single-component element
/// it is in the least significant bits (Annex C, figure C.3).
/// </description></item>
/// <item><description>12 bits, filled: one component per 16-bit word, above (method A) or below (method B) four padding bits.</description></item>
/// <item><description>10 and 12 bits, packed: a bit stream from the least significant bit of each word (figures C.2 and C.4).</description></item>
/// <item><description>32 and 64 bits: IEEE floating-point numbers in the byte order of the file.</description></item>
/// </list>
/// <para>
/// Integer components are scaled to the samples of the source layout: 1-bit components to 0 and 255, 8-bit ones as they
/// are, 10 and 12-bit ones to the nearest 16-bit sample, <c>(v × 65535 + max / 2) / max</c>, and 16-bit ones as they are.
/// 64-bit components are read in single precision when the conversion is exact, and rejected otherwise; a signaling NaN
/// of either depth becomes the quiet NaN of the same sign and payload. Cb, Y and Cr components are converted to R'G'B'
/// (<see cref="DpxYCbCrConverter"/>).
/// </para>
/// </remarks>
internal sealed class DpxRowReader
{
    private readonly bool _bigEndian;
    private readonly DpxDescriptor _descriptor;
    private readonly int _bitDepth;
    private readonly DpxPacking _packing;
    private readonly int _width;
    private readonly int _componentsPerLine;
    private readonly DpxYCbCrConverter? _yCbCr;

    public DpxRowReader(bool bigEndian, DpxDescriptor descriptor, int bitDepth, DpxPacking packing, int width, DpxYCbCrConverter? yCbCr)
    {
        _bigEndian = bigEndian;
        _descriptor = descriptor;
        _bitDepth = bitDepth;
        _packing = packing;
        _width = width;
        _yCbCr = yCbCr;
        _componentsPerLine = width * DpxFormat.GetComponentsPerPixel(descriptor);
        StoredLineLength = GetStoredLineLength(bitDepth, packing, (long)_componentsPerLine);
        SourcePixelFormat = PixelFormatWidening.GetWidenedFormat(GetDefaultPixelFormat(descriptor, bitDepth));
    }

    /// <summary>Gets the number of bytes of a stored line, without its end-of-line padding.</summary>
    public long StoredLineLength { get; }

    /// <summary>Gets the number of components of a line.</summary>
    public int ComponentsPerLine => _componentsPerLine;

    /// <summary>Gets the layout of the rows the reader writes.</summary>
    public PixelFormat SourcePixelFormat { get; }

    /// <summary>Gets whether the components are floating-point numbers.</summary>
    public bool IsFloatingPoint => _bitDepth >= 32;

    /// <summary>Gets the default pixel format of an image element: the smallest built-in format that holds its samples.</summary>
    public static PixelFormat GetDefaultPixelFormat(DpxDescriptor descriptor, int bitDepth)
    {
        if (bitDepth >= 32)
        {
            return descriptor switch
            {
                DpxDescriptor.Alpha => PixelFormat.LaFloat,
                DpxDescriptor.Red or DpxDescriptor.Green or DpxDescriptor.Blue or DpxDescriptor.Luma => PixelFormat.GrayFloat,
                _ => PixelFormat.RgbaFloat,
            };
        }

        var wide = bitDepth > 8;
        return descriptor switch
        {
            DpxDescriptor.Alpha => wide ? PixelFormat.La32 : PixelFormat.La16,
            DpxDescriptor.Red or DpxDescriptor.Green or DpxDescriptor.Blue or DpxDescriptor.Luma => wide ? PixelFormat.Gray16 : PixelFormat.Gray8,
            DpxDescriptor.Rgb or DpxDescriptor.CbYCrY or DpxDescriptor.CbYCr => wide ? PixelFormat.Rgb48 : PixelFormat.Rgb24,
            DpxDescriptor.Rgba or DpxDescriptor.Abgr or DpxDescriptor.CbYACrYA or DpxDescriptor.CbYCrA => wide ? PixelFormat.Rgba64 : PixelFormat.Rgba32,
            _ => throw new ArgumentOutOfRangeException(nameof(descriptor), descriptor, "The DPX descriptor is not decoded."),
        };
    }

    /// <summary>Gets the number of bytes of a stored line of <paramref name="components"/> components, a whole number of 32-bit words.</summary>
    public static long GetStoredLineLength(int bitDepth, DpxPacking packing, long components)
    {
        var words = bitDepth switch
        {
            1 => (components + 31) / 32,
            8 => (components + 3) / 4,
            10 when packing == DpxPacking.Packed => ((components * 10) + 31) / 32,
            10 => (components + 2) / 3,
            12 when packing == DpxPacking.Packed => ((components * 12) + 31) / 32,
            12 or 16 => (components + 1) / 2,
            32 => components,
            64 => components * 2,
            _ => throw new ArgumentOutOfRangeException(nameof(bitDepth), bitDepth, "The DPX bit depth is not valid."),
        };

        return words * 4;
    }

    /// <summary>Reads a stored line into a row of <see cref="SourcePixelFormat"/>.</summary>
    /// <param name="stored">The stored line: <see cref="StoredLineLength"/> bytes.</param>
    /// <param name="destination">The row.</param>
    /// <param name="scratch">A buffer of <see cref="ComponentsPerLine"/> components.</param>
    /// <exception cref="UnsupportedImageFeatureException">A 64-bit component has no exact single-precision value.</exception>
    public void ReadRow(ReadOnlySpan<byte> stored, Span<byte> destination, Span<uint> scratch)
    {
        var components = scratch[.._componentsPerLine];
        if (IsFloatingPoint)
        {
            ReadFloats(stored, components);
            WriteFloatPixels(components, unsafe(MemoryMarshal.Cast<byte, float>(destination)));
            return;
        }

        ReadComponents(stored, components);
        if (_yCbCr is { } converter)
        {
            converter.WriteRow(components, _width, destination);
            return;
        }

        if (SourcePixelFormat is PixelFormat.Rgba64 or PixelFormat.Gray16)
        {
            WriteWidePixels(components, unsafe(MemoryMarshal.Cast<byte, ushort>(destination)));
        }
        else
        {
            WriteNarrowPixels(components, destination);
        }
    }

    /// <summary>Gets the stored components of a line, as stored (not scaled), in the order of the descriptor.</summary>
    internal void ReadComponents(ReadOnlySpan<byte> stored, Span<uint> components)
    {
        switch (_bitDepth)
        {
            case 1:
                for (var i = 0; i < components.Length; i++)
                {
                    components[i] = (ReadWord(stored, i >> 5) >> (i & 31)) & 1;
                }

                break;

            case 8:
                for (var i = 0; i < components.Length; i++)
                {
                    components[i] = stored[i];
                }

                break;

            case 10 when _packing == DpxPacking.Packed:
                ReadBitStream(stored, components, 10);
                break;

            case 10:
            {
                // The position of the components in a word: the first one is at the top of a multi-component element
                var single = DpxFormat.IsSingleComponent(_descriptor);
                var top = _packing == DpxPacking.FilledMethodB ? 20 : 22;
                var bottom = _packing == DpxPacking.FilledMethodB ? 0 : 2;
                for (var i = 0; i < components.Length; i++)
                {
                    var word = ReadWord(stored, i / 3);
                    var index = i % 3;
                    var shift = single ? bottom + (10 * index) : top - (10 * index);
                    components[i] = (word >> shift) & 0x3FF;
                }

                break;
            }

            case 12 when _packing == DpxPacking.Packed:
                ReadBitStream(stored, components, 12);
                break;

            case 12:
            {
                var shift = _packing == DpxPacking.FilledMethodB ? 0 : 4;
                for (var i = 0; i < components.Length; i++)
                {
                    components[i] = (uint)(ReadHalfWord(stored, i) >> shift) & 0xFFF;
                }

                break;
            }

            case 16:
                for (var i = 0; i < components.Length; i++)
                {
                    components[i] = ReadHalfWord(stored, i);
                }

                break;

            default:
                throw new InvalidOperationException("The DPX bit depth is not an integer depth.");
        }
    }

    private void ReadFloats(ReadOnlySpan<byte> stored, Span<uint> components)
    {
        if (_bitDepth == 32)
        {
            for (var i = 0; i < components.Length; i++)
            {
                components[i] = QuietNaN(ReadWord(stored, i));
            }

            return;
        }

        for (var i = 0; i < components.Length; i++)
        {
            var bits = _bigEndian ? BinaryPrimitives.ReadUInt64BigEndian(stored[(i * 8)..]) : BinaryPrimitives.ReadUInt64LittleEndian(stored[(i * 8)..]);
            var value = BitConverter.Int64BitsToDouble((long)bits);
            var single = (float)value;
            if (!(single == value || (double.IsNaN(value) && float.IsNaN(single))))
                throw DpxFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The 64-bit component {value:R} has no exact single-precision value: the floating-point pixel formats of the library hold 32-bit samples, and rounding would lose precision."), "64-bit floating-point components that single precision does not hold");

            components[i] = BitConverter.SingleToUInt32Bits(single);
        }
    }

    /// <summary>
    /// Sets the quiet bit of a signaling NaN, keeping its sign and payload, as a 64-bit component read in single precision
    /// gets it: the conversions to the default pixel format of a gray or alpha element go through double precision, which
    /// quiets a signaling NaN, so that a sequential reader of <see cref="SourcePixelFormat"/> would otherwise give other
    /// bits than a load.
    /// </summary>
    private static uint QuietNaN(uint bits) => (bits & 0x7F80_0000) == 0x7F80_0000 && (bits & 0x007F_FFFF) != 0 ? bits | 0x0040_0000 : bits;

    private void ReadBitStream(ReadOnlySpan<byte> stored, Span<uint> components, int bits)
    {
        var mask = (1UL << bits) - 1;
        for (var i = 0; i < components.Length; i++)
        {
            var offset = (long)i * bits;
            var wordIndex = (int)(offset >> 5);
            var shift = (int)(offset & 31);
            ulong value = ReadWord(stored, wordIndex);
            if (shift + bits > 32)
            {
                value |= (ulong)ReadWord(stored, wordIndex + 1) << 32;
            }

            components[i] = (uint)((value >> shift) & mask);
        }
    }

    private uint ReadWord(ReadOnlySpan<byte> stored, int index)
        => _bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(stored[(index * 4)..]) : BinaryPrimitives.ReadUInt32LittleEndian(stored[(index * 4)..]);

    private ushort ReadHalfWord(ReadOnlySpan<byte> stored, int index)
        => _bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(stored[(index * 2)..]) : BinaryPrimitives.ReadUInt16LittleEndian(stored[(index * 2)..]);

    private void WriteNarrowPixels(ReadOnlySpan<uint> components, Span<byte> destination)
    {
        var scale = _bitDepth == 1 ? 255u : 1u;
        switch (_descriptor)
        {
            case DpxDescriptor.Red or DpxDescriptor.Green or DpxDescriptor.Blue or DpxDescriptor.Luma:
                for (var x = 0; x < _width; x++)
                {
                    destination[x] = (byte)(components[x] * scale);
                }

                break;

            case DpxDescriptor.Alpha:
                for (var x = 0; x < _width; x++)
                {
                    destination.Slice(x * 4, 4).Clear();
                    destination[(x * 4) + 3] = (byte)(components[x] * scale);
                }

                break;

            case DpxDescriptor.Rgb:
                for (var i = 0; i < _width * 3; i++)
                {
                    destination[i] = (byte)(components[i] * scale);
                }

                break;

            case DpxDescriptor.Rgba:
                for (var i = 0; i < _width * 4; i++)
                {
                    destination[i] = (byte)(components[i] * scale);
                }

                break;

            case DpxDescriptor.Abgr:
                for (var x = 0; x < _width; x++)
                {
                    var i = x * 4;
                    destination[i] = (byte)(components[i + 3] * scale);
                    destination[i + 1] = (byte)(components[i + 2] * scale);
                    destination[i + 2] = (byte)(components[i + 1] * scale);
                    destination[i + 3] = (byte)(components[i] * scale);
                }

                break;

            default:
                throw new InvalidOperationException("The DPX descriptor has no integer layout.");
        }
    }

    private void WriteWidePixels(ReadOnlySpan<uint> components, Span<ushort> destination)
    {
        var max = (1u << _bitDepth) - 1;
        switch (_descriptor)
        {
            case DpxDescriptor.Red or DpxDescriptor.Green or DpxDescriptor.Blue or DpxDescriptor.Luma:
                for (var x = 0; x < _width; x++)
                {
                    destination[x] = Scale(components[x], max);
                }

                break;

            case DpxDescriptor.Alpha:
                for (var x = 0; x < _width; x++)
                {
                    destination.Slice(x * 4, 3).Clear();
                    destination[(x * 4) + 3] = Scale(components[x], max);
                }

                break;

            case DpxDescriptor.Rgb:
                for (var x = 0; x < _width; x++)
                {
                    destination[x * 4] = Scale(components[x * 3], max);
                    destination[(x * 4) + 1] = Scale(components[(x * 3) + 1], max);
                    destination[(x * 4) + 2] = Scale(components[(x * 3) + 2], max);
                    destination[(x * 4) + 3] = ushort.MaxValue;
                }

                break;

            case DpxDescriptor.Rgba:
                for (var i = 0; i < _width * 4; i++)
                {
                    destination[i] = Scale(components[i], max);
                }

                break;

            case DpxDescriptor.Abgr:
                for (var x = 0; x < _width; x++)
                {
                    var i = x * 4;
                    destination[i] = Scale(components[i + 3], max);
                    destination[i + 1] = Scale(components[i + 2], max);
                    destination[i + 2] = Scale(components[i + 1], max);
                    destination[i + 3] = Scale(components[i], max);
                }

                break;

            default:
                throw new InvalidOperationException("The DPX descriptor has no integer layout.");
        }
    }

    private void WriteFloatPixels(ReadOnlySpan<uint> components, Span<float> destination)
    {
        switch (_descriptor)
        {
            case DpxDescriptor.Red or DpxDescriptor.Green or DpxDescriptor.Blue or DpxDescriptor.Luma:
                for (var x = 0; x < _width; x++)
                {
                    var value = BitConverter.UInt32BitsToSingle(components[x]);
                    destination[x * 4] = value;
                    destination[(x * 4) + 1] = value;
                    destination[(x * 4) + 2] = value;
                    destination[(x * 4) + 3] = 1f;
                }

                break;

            case DpxDescriptor.Alpha:
                for (var x = 0; x < _width; x++)
                {
                    destination.Slice(x * 4, 3).Clear();
                    destination[(x * 4) + 3] = BitConverter.UInt32BitsToSingle(components[x]);
                }

                break;

            case DpxDescriptor.Rgb:
                for (var x = 0; x < _width; x++)
                {
                    destination[x * 4] = BitConverter.UInt32BitsToSingle(components[x * 3]);
                    destination[(x * 4) + 1] = BitConverter.UInt32BitsToSingle(components[(x * 3) + 1]);
                    destination[(x * 4) + 2] = BitConverter.UInt32BitsToSingle(components[(x * 3) + 2]);
                    destination[(x * 4) + 3] = 1f;
                }

                break;

            case DpxDescriptor.Rgba:
                for (var i = 0; i < _width * 4; i++)
                {
                    destination[i] = BitConverter.UInt32BitsToSingle(components[i]);
                }

                break;

            case DpxDescriptor.Abgr:
                for (var x = 0; x < _width; x++)
                {
                    var i = x * 4;
                    destination[i] = BitConverter.UInt32BitsToSingle(components[i + 3]);
                    destination[i + 1] = BitConverter.UInt32BitsToSingle(components[i + 2]);
                    destination[i + 2] = BitConverter.UInt32BitsToSingle(components[i + 1]);
                    destination[i + 3] = BitConverter.UInt32BitsToSingle(components[i]);
                }

                break;

            default:
                throw new InvalidOperationException("The DPX descriptor has no floating-point layout.");
        }
    }

    /// <summary>Scales a component of <paramref name="max"/> to the nearest 16-bit sample.</summary>
    internal static ushort Scale(uint value, uint max) => max == ushort.MaxValue ? (ushort)value : (ushort)(((value * 65535UL) + (max / 2)) / max);
}
