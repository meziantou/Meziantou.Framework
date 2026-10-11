using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Meziantou.Framework.Imaging.TestHarness.JpegXl;

/// <summary>
/// An independent reader of the structure of a JPEG XL file: the boxes of the container, the image header of the
/// codestream and its frame headers. It never uses the library under test and never decodes a pixel.
/// </summary>
/// <remarks>
/// It reads what is stored as plain bit fields. The ICC profile of a codestream and a permuted table of contents are
/// entropy coded: an ICC profile is reported as present (<see cref="HasIccProfile"/>) and not read, and the frames that
/// follow one, or that follow a permuted table of contents, are not walked (<see cref="Frames"/> is then
/// <see langword="null"/>).
/// </remarks>
public sealed class ReferenceJpegXl
{
    private static readonly byte[] ContainerSignature = [0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A];

    private ReferenceJpegXl()
    {
    }

    /// <summary>Gets a value indicating whether the file is a container (boxes), not a bare codestream.</summary>
    public bool IsContainer { get; private set; }

    /// <summary>Gets the types of the boxes of the container, in file order.</summary>
    public IList<string> BoxTypes { get; } = [];

    /// <summary>Gets the codestream: the file itself, or the payloads of its <c>jxlc</c> or <c>jxlp</c> boxes.</summary>
    public ReadOnlyMemory<byte> Codestream { get; private set; }

    /// <summary>Gets the TIFF payload of the first <c>Exif</c> box (after its 4-byte offset), compressed or not.</summary>
    public ReadOnlyMemory<byte>? Exif { get; private set; }

    /// <summary>Gets the payload of the first <c>xml </c> box, compressed or not.</summary>
    public ReadOnlyMemory<byte>? Xmp { get; private set; }

    /// <summary>Gets the level of the <c>jxll</c> box, if any.</summary>
    public int? Level { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public int BitsPerSample { get; private set; } = 8;

    public bool HasFloatSamples { get; private set; }

    /// <summary>Gets the orientation of the image header (1 to 8, the values of the EXIF orientation).</summary>
    public int Orientation { get; private set; } = 1;

    public bool HasPreview { get; private set; }

    public bool HasAnimation { get; private set; }

    public long TicksPerSecondNumerator { get; private set; }

    public long TicksPerSecondDenominator { get; private set; }

    /// <summary>Gets the <c>num_loops</c> field: 0 repeats forever.</summary>
    public long LoopCount { get; private set; }

    public bool HasTimecodes { get; private set; }

    /// <summary>Gets the position of the <c>num_loops</c> field of an animation, in bits from the start of the codestream.</summary>
    public long? LoopCountBitOffset { get; private set; }

    /// <summary>
    /// Gets the position of the end of the image header (the padding that precedes the first frame excluded), in bits from the
    /// start of the codestream, or <see langword="null"/> when an ICC profile follows the header.
    /// </summary>
    public long? HeaderEndBitOffset { get; private set; }

    public IList<ReferenceJpegXlExtraChannel> ExtraChannels { get; } = [];

    public bool IsXybEncoded { get; private set; } = true;

    /// <summary>Gets a value indicating whether an ICC profile follows the image header.</summary>
    public bool HasIccProfile { get; private set; }

    /// <summary>Gets the <c>colour_space</c> field: 0 RGB, 1 grayscale, 2 XYB, 3 unknown.</summary>
    public int ColorSpace { get; private set; }

    /// <summary>Gets the <c>white_point</c> field: 1 D65, 2 custom, 10 E, 11 DCI.</summary>
    public int WhitePoint { get; private set; } = 1;

    /// <summary>Gets the <c>primaries</c> field: 1 sRGB, 2 custom, 9 BT.2100, 11 P3.</summary>
    public int Primaries { get; private set; } = 1;

    /// <summary>Gets the exponent of the power transfer function in units of 1e-7, or <see langword="null"/> when the transfer function is enumerated.</summary>
    public long? Gamma { get; private set; }

    /// <summary>Gets the <c>transfer_function</c> field (a code point of ITU-T H.273), when <see cref="Gamma"/> is <see langword="null"/>.</summary>
    public int TransferFunction { get; private set; } = 13;

    public int RenderingIntent { get; private set; } = 1;

    /// <summary>
    /// Gets the frames whose header was read, the preview excluded: every frame, or the frames up to the first one whose table
    /// of contents is permuted (<see cref="ReferenceJpegXlFrame.ByteCount"/> is then -1). Empty when an ICC profile follows the
    /// image header.
    /// </summary>
    public IList<ReferenceJpegXlFrame> FrameHeaders { get; } = [];

    /// <summary>Gets the frames of the codestream, the preview excluded, or <see langword="null"/> when they cannot be walked (see the remarks of the type).</summary>
    public IList<ReferenceJpegXlFrame>? Frames { get; private set; }

    /// <summary>Gets the frames a viewer shows: the regular frames that end the image or, in an animation, that have a duration.</summary>
    /// <exception cref="InvalidDataException">The frames are not read.</exception>
    public IReadOnlyList<ReferenceJpegXlFrame> GetDisplayedFrames()
        => [.. (Frames ?? throw new InvalidDataException("The frames of the codestream are not read.")).Where(frame => frame.Type is 0 or 3 && (frame.IsLast || (HasAnimation && frame.Duration > 0)))];

    /// <summary>Reads a file.</summary>
    /// <exception cref="InvalidDataException">The structure is malformed.</exception>
    public static ReferenceJpegXl Parse(ReadOnlySpan<byte> data)
    {
        var result = new ReferenceJpegXl();
        if (data.StartsWith(ContainerSignature))
        {
            result.IsContainer = true;
            result.ReadBoxes(data);
        }
        else
        {
            result.Codestream = data.ToArray();
        }

        result.ReadCodestream();
        return result;
    }

    private void ReadBoxes(ReadOnlySpan<byte> data)
    {
        using var codestream = new MemoryStream();
        var position = 0;
        while (position < data.Length)
        {
            if (data.Length - position < 8)
                throw new InvalidDataException("A box header is truncated.");

            long size = BinaryPrimitives.ReadUInt32BigEndian(data[position..]);
            var type = Encoding.Latin1.GetString(data.Slice(position + 4, 4));
            var headerSize = 8;
            if (size == 1)
            {
                if (data.Length - position < 16)
                    throw new InvalidDataException("A box header is truncated.");

                size = checked((long)BinaryPrimitives.ReadUInt64BigEndian(data[(position + 8)..]));
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = data.Length - position;
            }

            if (size < headerSize || size > data.Length - position)
                throw new InvalidDataException($"The box '{type}' does not fit the file.");

            var payload = data.Slice(position + headerSize, (int)size - headerSize);
            BoxTypes.Add(type);
            if (type == "brob")
            {
                if (payload.Length < 4)
                    throw new InvalidDataException("A brob box has no type.");

                type = Encoding.Latin1.GetString(payload[..4]);
                using var decoded = new MemoryStream();
                using (var brotli = new BrotliStream(new MemoryStream(payload[4..].ToArray()), CompressionMode.Decompress))
                {
                    brotli.CopyTo(decoded);
                }

                AddBox(type, decoded.ToArray(), codestream);
            }
            else
            {
                AddBox(type, payload, codestream);
            }

            position += (int)size;
        }

        Codestream = codestream.ToArray();
    }

    private void AddBox(string type, ReadOnlySpan<byte> payload, MemoryStream codestream)
    {
        switch (type)
        {
            case "jxlc":
                codestream.Write(payload);
                break;
            case "jxlp":
                if (payload.Length < 4)
                    throw new InvalidDataException("A jxlp box has no index.");

                codestream.Write(payload[4..]);
                break;
            case "jxll" when payload.Length == 1:
                Level = payload[0];
                break;
            case "Exif" when Exif is null:
                if (payload.Length < 4)
                    throw new InvalidDataException("An Exif box has no offset.");

                var offset = BinaryPrimitives.ReadUInt32BigEndian(payload);
                if (offset > payload.Length - 4)
                    throw new InvalidDataException("The offset of an Exif box is outside the box.");

                Exif = payload[(4 + (int)offset)..].ToArray();
                break;
            case "xml " when Xmp is null:
                Xmp = payload.ToArray();
                break;
        }
    }

    private void ReadCodestream()
    {
        var reader = new BitReader(Codestream.ToArray());
        if (reader.Bits(16) != 0x0AFF)
            throw new InvalidDataException("The codestream does not start with its signature.");

        (Width, Height) = ReadSize(reader);
        if (!reader.Bool())
        {
            var extraFields = reader.Bool();
            if (extraFields)
            {
                Orientation = (int)reader.Bits(3) + 1;
                if (reader.Bool())
                {
                    _ = ReadSize(reader);
                }

                HasPreview = reader.Bool();
                if (HasPreview)
                {
                    ReadPreviewSize(reader);
                }

                HasAnimation = reader.Bool();
                if (HasAnimation)
                {
                    TicksPerSecondNumerator = reader.U32((100, 0), (1000, 0), (1, 10), (1, 30));
                    TicksPerSecondDenominator = reader.U32((1, 0), (1001, 0), (1, 8), (1, 10));
                    LoopCountBitOffset = reader.Position;
                    LoopCount = reader.U32((0, 0), (0, 3), (0, 16), (0, 32));
                    HasTimecodes = reader.Bool();
                }
            }

            (BitsPerSample, HasFloatSamples) = ReadBitDepth(reader);
            _ = reader.Bool();
            var extraChannelCount = reader.U32((0, 0), (1, 0), (2, 4), (1, 12));
            for (var i = 0; i < extraChannelCount; i++)
            {
                ExtraChannels.Add(ReadExtraChannel(reader));
            }

            IsXybEncoded = reader.Bool();
            ReadColorEncoding(reader);
            if (extraFields && !reader.Bool())
            {
                // Tone mapping: the intensity target, the darkest level, a flag and the linear threshold
                reader.Skip(16 + 16 + 1 + 16);
            }

            SkipExtensions(reader);
        }

        // The data of the transforms: the inverse opsin matrix of an XYB image and the weights of the upsampling filters
        if (!reader.Bool())
        {
            if (IsXybEncoded && !reader.Bool())
            {
                reader.Skip(16 * 16);
            }

            var mask = reader.Bits(3);
            reader.Skip(16 * (((mask & 1) != 0 ? 15 : 0) + ((mask & 2) != 0 ? 55 : 0) + ((mask & 4) != 0 ? 210 : 0)));
        }

        if (HasIccProfile)
            return;

        HeaderEndBitOffset = reader.Position;
        if (HasPreview)
        {
            reader.Align();
            if (ReadFrame(reader).ByteCount < 0)
                return;
        }

        while (true)
        {
            reader.Align();
            var frame = ReadFrame(reader);
            FrameHeaders.Add(frame);
            if (frame.ByteCount < 0)
                return;

            if (frame.IsLast)
                break;
        }

        Frames = FrameHeaders;
    }

    private static (int Width, int Height) ReadSize(BitReader reader)
    {
        var small = reader.Bool();
        var height = small ? (reader.Bits(5) + 1) * 8 : reader.U32((1, 9), (1, 13), (1, 18), (1, 30));
        var ratio = reader.Bits(3);
        var width = ratio switch
        {
            0 => small ? (reader.Bits(5) + 1) * 8 : reader.U32((1, 9), (1, 13), (1, 18), (1, 30)),
            1 => height,
            2 => height * 12 / 10,
            3 => height * 4 / 3,
            4 => height * 3 / 2,
            5 => height * 16 / 9,
            6 => height * 5 / 4,
            _ => height * 2,
        };
        return (checked((int)width), checked((int)height));
    }

    private static void ReadPreviewSize(BitReader reader)
    {
        var small = reader.Bool();
        if (small)
        {
            _ = reader.U32((16, 0), (32, 0), (1, 5), (33, 9));
        }
        else
        {
            _ = reader.U32((1, 6), (65, 8), (321, 10), (1345, 12));
        }

        if (reader.Bits(3) == 0)
        {
            if (small)
            {
                _ = reader.U32((16, 0), (32, 0), (1, 5), (33, 9));
            }
            else
            {
                _ = reader.U32((1, 6), (65, 8), (321, 10), (1345, 12));
            }
        }
    }

    private static (int Bits, bool IsFloat) ReadBitDepth(BitReader reader)
    {
        if (!reader.Bool())
            return ((int)reader.U32((8, 0), (10, 0), (12, 0), (1, 6)), false);

        var bits = (int)reader.U32((32, 0), (16, 0), (24, 0), (1, 6));
        reader.Skip(4);
        return (bits, true);
    }

    private static ReferenceJpegXlExtraChannel ReadExtraChannel(BitReader reader)
    {
        // The default extra channel is an 8-bit alpha channel of the size of the image
        if (reader.Bool())
            return new ReferenceJpegXlExtraChannel(Type: 0, BitsPerSample: 8, DimShift: 0, IsAlphaAssociated: false);

        var type = ReadEnum(reader);
        var (bits, _) = ReadBitDepth(reader);
        var dimShift = (int)reader.U32((0, 0), (3, 0), (4, 0), (1, 3));
        SkipName(reader);
        var associated = type == 0 && reader.Bool();
        if (type == 2)
        {
            reader.Skip(4 * 16);
        }
        else if (type == 5)
        {
            _ = reader.U32((1, 0), (0, 2), (3, 4), (19, 8));
        }

        return new ReferenceJpegXlExtraChannel(type, bits, dimShift, associated);
    }

    private void ReadColorEncoding(BitReader reader)
    {
        if (reader.Bool())
        {
            ColorSpace = 0;
            return;
        }

        HasIccProfile = reader.Bool();
        ColorSpace = ReadEnum(reader);
        if (HasIccProfile)
            return;

        if (ColorSpace != 2)
        {
            WhitePoint = ReadEnum(reader);
            if (WhitePoint == 2)
            {
                SkipCustomXy(reader);
            }

            if (ColorSpace != 1)
            {
                Primaries = ReadEnum(reader);
                if (Primaries == 2)
                {
                    SkipCustomXy(reader);
                    SkipCustomXy(reader);
                    SkipCustomXy(reader);
                }
            }
        }

        if (ColorSpace != 2)
        {
            if (reader.Bool())
            {
                Gamma = reader.Bits(24);
            }
            else
            {
                TransferFunction = ReadEnum(reader);
            }
        }

        RenderingIntent = ReadEnum(reader);
    }

    private static void SkipCustomXy(BitReader reader)
    {
        _ = reader.U32((0, 19), (524288, 19), (1048576, 20), (2097152, 21));
        _ = reader.U32((0, 19), (524288, 19), (1048576, 20), (2097152, 21));
    }

    private static int ReadEnum(BitReader reader) => (int)reader.U32((0, 0), (1, 0), (2, 4), (18, 6));

    private static void SkipName(BitReader reader)
    {
        var length = reader.U32((0, 0), (0, 4), (16, 5), (48, 10));
        reader.Skip(8 * length);
    }

    private static void SkipExtensions(BitReader reader)
    {
        var extensions = reader.U64();
        ulong total = 0;
        for (var i = 0; i < 64; i++)
        {
            if ((extensions & (1UL << i)) != 0)
            {
                total = checked(total + reader.U64());
            }
        }

        reader.Skip(checked((long)total));
    }

    /// <summary>Reads a frame header and its table of contents, and skips the sections of the frame.</summary>
    /// <returns>The frame; its size is -1 when its table of contents is permuted: the frames are not walked further.</returns>
    private ReferenceJpegXlFrame ReadFrame(BitReader reader)
    {
        var type = 0;
        var isModular = false;
        ulong flags = 0;
        var isYCbCr = false;
        var upsampling = 1;
        var groupSizeShift = 1;
        var passCount = 1;
        var lfLevel = 0;
        var x = 0;
        var y = 0;
        long width = Width;
        long height = Height;
        var blendMode = 0;
        long duration = 0;
        var isLast = true;
        var saveAsReference = 0;
        if (!reader.Bool())
        {
            type = (int)reader.Bits(2);
            isModular = reader.Bool();
            flags = reader.U64();
            isYCbCr = !IsXybEncoded && reader.Bool();
            if ((flags & 0x20) == 0)
            {
                if (isYCbCr)
                {
                    reader.Skip(6);
                }

                upsampling = 1 << (int)reader.Bits(2);
                reader.Skip(2 * ExtraChannels.Count);
            }

            if (isModular)
            {
                groupSizeShift = (int)reader.Bits(2);
            }
            else if (IsXybEncoded)
            {
                reader.Skip(6);
            }

            if (type != 2)
            {
                passCount = (int)reader.U32((1, 0), (2, 0), (3, 0), (4, 3));
                if (passCount != 1)
                {
                    var downsampleCount = reader.U32((0, 0), (1, 0), (2, 0), (3, 1));
                    reader.Skip(2 * (passCount - 1));
                    reader.Skip(2 * downsampleCount);
                    for (var i = 0; i < downsampleCount; i++)
                    {
                        _ = reader.U32((0, 0), (1, 0), (2, 0), (0, 3));
                    }
                }
            }

            if (type == 1)
            {
                lfLevel = (int)reader.Bits(2) + 1;
            }

            var isPartial = false;
            if (type != 1 && reader.Bool())
            {
                if (type is 0 or 3)
                {
                    x = UnpackSigned(reader.U32((0, 8), (256, 11), (2304, 14), (18688, 30)));
                    y = UnpackSigned(reader.U32((0, 8), (256, 11), (2304, 14), (18688, 30)));
                }

                width = reader.U32((0, 8), (256, 11), (2304, 14), (18688, 30));
                height = reader.U32((0, 8), (256, 11), (2304, 14), (18688, 30));
                isPartial = type is 0 or 3 && (x > 0 || y > 0 || width + x < Width || height + y < Height);
            }

            isLast = false;
            if (type is 0 or 3)
            {
                blendMode = ReadBlending(reader, isPartial);
                for (var i = 0; i < ExtraChannels.Count; i++)
                {
                    _ = ReadBlending(reader, isPartial);
                }

                if (HasAnimation)
                {
                    duration = reader.U32((0, 0), (1, 0), (0, 8), (0, 32));
                    if (HasTimecodes)
                    {
                        reader.Skip(32);
                    }
                }

                isLast = reader.Bool();
            }

            if (type != 1 && !isLast)
            {
                saveAsReference = (int)reader.Bits(2);
            }

            if (type == 2)
            {
                reader.Skip(1);
            }
            else if (type != 1 && !isLast && (duration == 0 || saveAsReference != 0) && blendMode == 0 && !isPartial)
            {
                reader.Skip(1);
            }

            SkipName(reader);
            ReadRestorationFilter(reader, isModular);
            SkipExtensions(reader);
        }

        if (lfLevel != 0)
        {
            // An LF frame of level n has the size of the image divided by 8^n
            var divisor = 1L << (3 * lfLevel);
            width = (width + divisor - 1) / divisor;
            height = (height + divisor - 1) / divisor;
        }

        // The sections: one for a frame of one group and one pass; otherwise the LF global section, one per LF group, the HF
        // global section and one per group and pass
        var codedWidth = (width + upsampling - 1) / upsampling;
        var codedHeight = (height + upsampling - 1) / upsampling;
        var groupDimension = 128L << groupSizeShift;
        var groupCount = ((codedWidth + groupDimension - 1) / groupDimension) * ((codedHeight + groupDimension - 1) / groupDimension);
        var lfGroupDimension = groupDimension * 8;
        var lfGroupCount = ((codedWidth + lfGroupDimension - 1) / lfGroupDimension) * ((codedHeight + lfGroupDimension - 1) / lfGroupDimension);
        var sectionCount = groupCount == 1 && passCount == 1 ? 1 : 2 + lfGroupCount + (groupCount * passCount);
        long byteCount = -1;
        if (!reader.Bool())
        {
            reader.Align();
            byteCount = 0;
            for (var i = 0; i < sectionCount; i++)
            {
                byteCount += reader.U32((0, 10), (1024, 14), (17408, 22), (4211712, 30));
            }

            reader.Align();
            reader.Skip(8 * byteCount);
        }

        return new ReferenceJpegXlFrame(type, isModular, flags, isYCbCr, upsampling, passCount, x, y, (int)width, (int)height, blendMode, duration, isLast, saveAsReference, (int)sectionCount, byteCount);
    }

    private int ReadBlending(BitReader reader, bool isPartial)
    {
        // 0 replace, 1 add, 2 blend, 3 alpha-weighted add, 4 multiply
        var mode = (int)reader.U32((0, 0), (1, 0), (2, 0), (3, 2));
        var usesAlpha = ExtraChannels.Count > 0 && mode is 2 or 3;
        if (usesAlpha)
        {
            _ = reader.U32((0, 0), (1, 0), (2, 0), (3, 3));
        }

        if (usesAlpha || mode == 4)
        {
            reader.Skip(1);
        }

        if (mode != 0 || isPartial)
        {
            reader.Skip(2);
        }

        return mode;
    }

    private static void ReadRestorationFilter(BitReader reader, bool isModular)
    {
        if (reader.Bool())
            return;

        if (reader.Bool() && reader.Bool())
        {
            reader.Skip(6 * 16);
        }

        var iterations = reader.Bits(2);
        if (iterations > 0)
        {
            if (!isModular && reader.Bool())
            {
                reader.Skip(8 * 16);
            }

            if (reader.Bool())
            {
                reader.Skip((3 * 16) + 32);
            }

            if (reader.Bool())
            {
                reader.Skip(((isModular ? 0 : 1) + 3) * 16);
            }

            if (isModular)
            {
                reader.Skip(16);
            }
        }

        SkipExtensions(reader);
    }

    private static int UnpackSigned(long value) => (int)((value >> 1) ^ -(value & 1));

    /// <summary>Reads the bits of the codestream, least significant bit of each byte first.</summary>
    private sealed class BitReader(byte[] data)
    {
        private long _position;

        public long Position => _position;

        public long Bits(int count)
        {
            long value = 0;
            for (var i = 0; i < count; i++)
            {
                var index = _position >> 3;
                if (index >= data.Length)
                    throw new InvalidDataException("The codestream is truncated.");

                value |= (long)((data[index] >> (int)(_position & 7)) & 1) << i;
                _position++;
            }

            return value;
        }

        public bool Bool() => Bits(1) != 0;

        public long U32((long Offset, int Bits) d0, (long Offset, int Bits) d1, (long Offset, int Bits) d2, (long Offset, int Bits) d3)
        {
            var (offset, bits) = Bits(2) switch
            {
                0 => d0,
                1 => d1,
                2 => d2,
                _ => d3,
            };
            return offset + Bits(bits);
        }

        public ulong U64()
        {
            switch (Bits(2))
            {
                case 0:
                    return 0;
                case 1:
                    return (ulong)(1 + Bits(4));
                case 2:
                    return (ulong)(17 + Bits(8));
            }

            var value = (ulong)Bits(12);
            var shift = 12;
            while (Bool())
            {
                if (shift == 60)
                {
                    value |= (ulong)Bits(4) << shift;
                    break;
                }

                value |= (ulong)Bits(8) << shift;
                shift += 8;
            }

            return value;
        }

        public void Skip(long count)
        {
            if (count < 0 || _position + count > data.Length * 8L)
                throw new InvalidDataException("The codestream is truncated.");

            _position += count;
        }

        public void Align()
        {
            while ((_position & 7) != 0)
            {
                if (Bits(1) != 0)
                    throw new InvalidDataException("The padding of the codestream is not zero.");
            }
        }
    }
}
