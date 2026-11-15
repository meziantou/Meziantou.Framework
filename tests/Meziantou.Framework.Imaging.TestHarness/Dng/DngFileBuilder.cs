using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text;

#pragma warning disable CA1819 // Properties should not return arrays

namespace Meziantou.Framework.Imaging.TestHarness.Dng;

/// <summary>
/// Assembles DNG files from synthetic sensor data, following the DNG specification 1.7.1 and TIFF 6.0, independently of
/// the library under test: a classic TIFF (either byte order) whose IFD 0 holds the DNG tags and the camera profile, and
/// the raw image itself or, with <see cref="ThumbnailWidth"/>, an 8-bit RGB thumbnail whose SubIFD is the raw image.
/// </summary>
/// <remarks>
/// The samples are stored uncompressed (8 and 16 bits as such, other widths packed most significant bit first, floats
/// in the byte order of the file) or with Deflate after a predictor (horizontal differencing with a distance of 1, 2 or
/// 4 pixels, the floating-point predictor of TIFF Technical Note 3), in strips or tiles. Numbers are written in the type
/// the specification gives each tag; rationals with a denominator of 10,000 (signed or not), so that a reader gets the
/// rounded value of the builder back exactly.
/// </remarks>
public sealed class DngFileBuilder
{
    /// <summary>The denominator of the rational values the builder writes.</summary>
    public const int RationalDenominator = 10000;

    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>Gets the stored samples, <see cref="SamplesPerPixel"/> per pixel, row by row: integers, or the values of floating-point samples.</summary>
    public required double[] Samples { get; init; }

    /// <summary>Gets the photometric interpretation: 32803 (CFA) or 34892 (LinearRaw).</summary>
    public int Photometric { get; init; } = 32803;

    public int SamplesPerPixel { get; init; } = 1;

    public int BitsPerSample { get; init; } = 16;

    /// <summary>Gets a value indicating whether the samples are IEEE floating-point numbers (16 or 32 bits).</summary>
    public bool IsFloatingPoint { get; init; }

    public bool IsBigEndian { get; init; }

    public int CfaRows { get; init; } = 2;

    public int CfaColumns { get; init; } = 2;

    /// <summary>Gets the CFA colors of the pattern, row by row (0 red, 1 green, 2 blue, ...): RGGB by default.</summary>
    public byte[] CfaPattern { get; init; } = [0, 1, 1, 2];

    public byte[]? CfaPlaneColor { get; init; }

    /// <summary>Gets the compression: 1 (none) or 8 (Deflate).</summary>
    public int Compression { get; init; } = 1;

    public int Predictor { get; init; } = 1;

    /// <summary>
    /// Gets the blocks of the raw image as the file stores them (compressed by the caller, with <see cref="Compression"/>),
    /// or <see langword="null"/> to encode <see cref="Samples"/>.
    /// </summary>
    public IReadOnlyList<byte[]>? EncodedBlocks { get; init; }

    /// <summary>Gets the size of the tiles, or <see langword="null"/> for strips.</summary>
    public (int Width, int Height)? Tile { get; init; }

    /// <summary>Gets the number of rows of a strip (0: the whole image in one strip).</summary>
    public int RowsPerStrip { get; init; }

    public byte[] DngVersion { get; init; } = [1, 4, 0, 0];

    public byte[]? DngBackwardVersion { get; init; }

    public string? Make { get; init; } = "Meziantou";

    public string? Model { get; init; } = "Synthetic";

    public string? UniqueCameraModel { get; init; } = "Meziantou Synthetic";

    public string? CameraSerialNumber { get; init; }

    public string? ProfileName { get; init; }

    public int Orientation { get; init; } = 1;

    /// <summary>Gets the active area: top, left, bottom, right.</summary>
    public int[]? ActiveArea { get; init; }

    public double[]? DefaultCropOrigin { get; init; }

    public double[]? DefaultCropSize { get; init; }

    public int[]? BlackLevelRepeatDim { get; init; }

    public double[]? BlackLevel { get; init; }

    public double[]? BlackLevelDeltaH { get; init; }

    public double[]? BlackLevelDeltaV { get; init; }

    public double[]? WhiteLevel { get; init; }

    public ushort[]? LinearizationTable { get; init; }

    public double[]? ColorMatrix1 { get; init; } = [1, 0, 0, 0, 1, 0, 0, 0, 1];

    public double[]? ColorMatrix2 { get; init; }

    public double[]? ForwardMatrix1 { get; init; }

    public double[]? ForwardMatrix2 { get; init; }

    public double[]? CameraCalibration1 { get; init; }

    public double[]? CameraCalibration2 { get; init; }

    public double[]? AnalogBalance { get; init; }

    public double[]? AsShotNeutral { get; init; } = [1, 1, 1];

    public double[]? AsShotWhiteXY { get; init; }

    public int? CalibrationIlluminant1 { get; init; } = 21;

    public int? CalibrationIlluminant2 { get; init; }

    public double? BaselineExposure { get; init; }

    public double? BaselineExposureOffset { get; init; }

    /// <summary>Gets <c>ProfileToneCurve</c> (pairs of input and output), written to make the rendering of the DNG SDK linear: (0, 0), (1, 1).</summary>
    public float[]? ProfileToneCurve { get; init; } = [0, 0, 1, 1];

    /// <summary>Gets <c>DefaultBlackRender</c> (1: none, so that the DNG SDK subtracts no flare).</summary>
    public int? DefaultBlackRender { get; init; } = 1;

    public byte[]? OpcodeList1 { get; init; }

    public byte[]? OpcodeList2 { get; init; }

    public byte[]? OpcodeList3 { get; init; }

    /// <summary>Gets the width of an 8-bit RGB thumbnail stored in IFD 0, the raw image being its SubIFD; 0 for none.</summary>
    public int ThumbnailWidth { get; init; }

    public int ThumbnailHeight { get; init; }

    public byte[]? Xmp { get; init; }

    /// <summary>Gets ASCII tags of an EXIF IFD that IFD 0 points to (tag and value), or <see langword="null"/> for none.</summary>
    public IReadOnlyList<(ushort Tag, string Value)>? ExifTags { get; init; }

    /// <summary>Gets extra tags of the raw IFD, which replace the tags of the same number the builder writes.</summary>
    public Collection<DngTag> ExtraRawTags { get; } = [];

    /// <summary>Gets extra tags of IFD 0, which replace the tags of the same number the builder writes.</summary>
    public Collection<DngTag> ExtraIfd0Tags { get; } = [];

    /// <summary>Gets the tags of the raw IFD the builder leaves out (to make an error fixture); the extra tags are kept.</summary>
    public HashSet<ushort> OmittedRawTags { get; } = [];

    /// <summary>Builds the file.</summary>
    public byte[] Build()
    {
        if (Samples.Length != Width * Height * SamplesPerPixel)
            throw new InvalidOperationException("The number of samples does not match the size of the image.");

        using var writer = new TiffWriter(IsBigEndian);
        var blocks = EncodedBlocks is { } encoded ? [.. encoded] : EncodeBlocks();
        var raw = new List<DngTag>
        {
            DngTag.Scalar(254, 0),
            DngTag.Scalar(256, (uint)Width),
            DngTag.Scalar(257, (uint)Height),
            DngTag.Shorts(258, [.. Enumerable.Repeat((ushort)BitsPerSample, SamplesPerPixel)]),
            DngTag.Shorts(259, (ushort)Compression),
            DngTag.Shorts(262, (ushort)Photometric),
            DngTag.Shorts(277, (ushort)SamplesPerPixel),
            DngTag.Shorts(284, 1),
        };

        if (IsFloatingPoint)
        {
            raw.Add(DngTag.Shorts(339, [.. Enumerable.Repeat((ushort)3, SamplesPerPixel)]));
        }

        if (Predictor != 1)
        {
            raw.Add(DngTag.Shorts(317, (ushort)Predictor));
        }

        if (Photometric == 32803)
        {
            raw.Add(DngTag.Shorts(33421, (ushort)CfaRows, (ushort)CfaColumns));
            raw.Add(DngTag.Bytes(33422, CfaPattern));
            if (CfaPlaneColor is not null)
            {
                raw.Add(DngTag.Bytes(50710, CfaPlaneColor));
            }

            raw.Add(DngTag.Shorts(50711, 1));
        }

        if (LinearizationTable is not null)
        {
            raw.Add(DngTag.Shorts(50712, LinearizationTable));
        }

        if (BlackLevelRepeatDim is not null)
        {
            raw.Add(DngTag.Shorts(50713, [.. BlackLevelRepeatDim.Select(value => (ushort)value)]));
        }

        if (BlackLevel is not null)
        {
            raw.Add(DngTag.Rationals(50714, BlackLevel));
        }

        if (BlackLevelDeltaH is not null)
        {
            raw.Add(DngTag.SignedRationals(50715, BlackLevelDeltaH));
        }

        if (BlackLevelDeltaV is not null)
        {
            raw.Add(DngTag.SignedRationals(50716, BlackLevelDeltaV));
        }

        if (WhiteLevel is not null)
        {
            raw.Add(DngTag.UInt32s(50717, [.. WhiteLevel.Select(value => (uint)value)]));
        }

        if (DefaultCropOrigin is not null)
        {
            raw.Add(DngTag.Rationals(50719, DefaultCropOrigin));
        }

        if (DefaultCropSize is not null)
        {
            raw.Add(DngTag.Rationals(50720, DefaultCropSize));
        }

        if (ActiveArea is not null)
        {
            raw.Add(DngTag.UInt32s(50829, [.. ActiveArea.Select(value => (uint)value)]));
        }

        AddUndefined(raw, 51008, OpcodeList1);
        AddUndefined(raw, 51009, OpcodeList2);
        AddUndefined(raw, 51022, OpcodeList3);
        raw.RemoveAll(tag => OmittedRawTags.Contains(tag.Tag) || ExtraRawTags.Any(extra => extra.Tag == tag.Tag));
        raw.AddRange(ExtraRawTags);

        var profile = new List<DngTag>
        {
            DngTag.Bytes(50706, DngVersion),
        };

        if (DngBackwardVersion is not null)
        {
            profile.Add(DngTag.Bytes(50707, DngBackwardVersion));
        }

        AddAscii(profile, 271, Make);
        AddAscii(profile, 272, Model);
        profile.Add(DngTag.Shorts(274, (ushort)Orientation));
        AddAscii(profile, 50708, UniqueCameraModel);
        AddAscii(profile, 50735, CameraSerialNumber);
        AddAscii(profile, 50936, ProfileName);
        AddSignedRationals(profile, 50721, ColorMatrix1);
        AddSignedRationals(profile, 50722, ColorMatrix2);
        AddSignedRationals(profile, 50723, CameraCalibration1);
        AddSignedRationals(profile, 50724, CameraCalibration2);
        AddRationals(profile, 50727, AnalogBalance);
        AddRationals(profile, 50728, AsShotNeutral);
        AddRationals(profile, 50729, AsShotWhiteXY);
        if (BaselineExposure is { } exposure)
        {
            profile.Add(DngTag.SignedRationals(50730, [exposure]));
        }

        if (CalibrationIlluminant1 is { } illuminant1)
        {
            profile.Add(DngTag.Shorts(50778, (ushort)illuminant1));
        }

        if (CalibrationIlluminant2 is { } illuminant2)
        {
            profile.Add(DngTag.Shorts(50779, (ushort)illuminant2));
        }

        if (ProfileToneCurve is not null)
        {
            profile.Add(DngTag.Floats(50940, ProfileToneCurve));
        }

        AddSignedRationals(profile, 50964, ForwardMatrix1);
        AddSignedRationals(profile, 50965, ForwardMatrix2);
        if (BaselineExposureOffset is { } offset)
        {
            profile.Add(DngTag.SignedRationals(51109, [offset]));
        }

        if (DefaultBlackRender is { } blackRender)
        {
            profile.Add(DngTag.Scalar(51110, (uint)blackRender));
        }

        if (Xmp is not null)
        {
            profile.Add(DngTag.Bytes(700, Xmp));
        }

        profile.RemoveAll(tag => ExtraIfd0Tags.Any(extra => extra.Tag == tag.Tag));
        profile.AddRange(ExtraIfd0Tags);

        // The blocks of the raw image are written first, after the header; then the thumbnail, then the directories
        writer.Position = 8;
        var blockOffsets = new uint[blocks.Count];
        for (var i = 0; i < blocks.Count; i++)
        {
            blockOffsets[i] = writer.Append(blocks[i]);
        }

        // The extra tags replace these ones too
        var isTiled = Tile is not null;
        raw.Add(DngTag.UInt32s(isTiled ? (ushort)324 : (ushort)273, blockOffsets));
        raw.Add(DngTag.UInt32s(isTiled ? (ushort)325 : (ushort)279, [.. blocks.Select(block => (uint)block.Length)]));
        raw.RemoveAll(tag => tag.Tag is 273 or 279 or 324 or 325 && !ExtraRawTags.Contains(tag) && ExtraRawTags.Any(extra => extra.Tag == tag.Tag));
        if (Tile is { } tile)
        {
            raw.Add(DngTag.Scalar(322, (uint)tile.Width));
            raw.Add(DngTag.Scalar(323, (uint)tile.Height));
        }
        else
        {
            raw.Add(DngTag.Scalar(278, (uint)GetRowsPerStrip()));
        }

        uint? exifOffset = null;
        if (ExifTags is not null)
        {
            exifOffset = writer.WriteDirectory([.. ExifTags.Select(entry => DngTag.Ascii(entry.Tag, entry.Value))], next: 0);
        }

        if (exifOffset is { } exif)
        {
            profile.Add(DngTag.Scalar(34665, exif));
        }

        uint firstDirectory;
        if (ThumbnailWidth > 0)
        {
            var pixels = new byte[ThumbnailWidth * ThumbnailHeight * 3];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = (byte)((i * 7) % 251);
            }

            var thumbnailOffset = writer.Append(pixels);
            var rawOffset = writer.WriteDirectory(raw, next: 0);
            List<DngTag> thumbnail =
            [
                DngTag.Scalar(254, 1),
                DngTag.Scalar(256, (uint)ThumbnailWidth),
                DngTag.Scalar(257, (uint)ThumbnailHeight),
                DngTag.Shorts(258, 8, 8, 8),
                DngTag.Shorts(259, 1),
                DngTag.Shorts(262, 2),
                DngTag.Scalar(273, thumbnailOffset),
                DngTag.Shorts(277, 3),
                DngTag.Scalar(278, (uint)ThumbnailHeight),
                DngTag.Scalar(279, (uint)pixels.Length),
                DngTag.Shorts(284, 1),
                DngTag.Scalar(330, rawOffset),
                .. profile,
            ];
            firstDirectory = writer.WriteDirectory(thumbnail, next: 0);
        }
        else
        {
            firstDirectory = writer.WriteDirectory([.. raw, .. profile], next: 0);
        }

        return writer.ToArray(firstDirectory);
    }

    /// <summary>Gets the number of rows of a strip.</summary>
    public int GetRowsPerStrip() => RowsPerStrip > 0 ? RowsPerStrip : Height;

    /// <summary>Encodes a row of samples (stored values) the way the file stores them, before any predictor.</summary>
    public byte[] EncodeRow(ReadOnlySpan<double> samples)
    {
        if (IsFloatingPoint)
        {
            var bytesPerSample = BitsPerSample / 8;
            var result = new byte[samples.Length * bytesPerSample];
            for (var i = 0; i < samples.Length; i++)
            {
                var target = result.AsSpan(i * bytesPerSample);
                if (BitsPerSample == 16)
                {
                    var bits = BitConverter.HalfToUInt16Bits((Half)samples[i]);
                    if (IsBigEndian)
                    {
                        BinaryPrimitives.WriteUInt16BigEndian(target, bits);
                    }
                    else
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(target, bits);
                    }
                }
                else if (IsBigEndian)
                {
                    BinaryPrimitives.WriteSingleBigEndian(target, (float)samples[i]);
                }
                else
                {
                    BinaryPrimitives.WriteSingleLittleEndian(target, (float)samples[i]);
                }
            }

            return result;
        }

        if (BitsPerSample is 8 or 16)
        {
            var bytesPerSample = BitsPerSample / 8;
            var result = new byte[samples.Length * bytesPerSample];
            for (var i = 0; i < samples.Length; i++)
            {
                var value = checked((ushort)samples[i]);
                if (bytesPerSample == 1)
                {
                    result[i] = checked((byte)value);
                }
                else if (IsBigEndian)
                {
                    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(i * 2), value);
                }
                else
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(i * 2), value);
                }
            }

            return result;
        }

        // Packed most significant bit first, whatever the byte order of the file; each row ends on a byte
        var packed = new byte[((samples.Length * BitsPerSample) + 7) / 8];
        var position = 0;
        foreach (var sample in samples)
        {
            var value = checked((int)sample);
            if (value >= 1 << BitsPerSample)
                throw new InvalidOperationException("A sample does not fit in its bits.");

            for (var bit = BitsPerSample - 1; bit >= 0; bit--, position++)
            {
                if (((value >> bit) & 1) != 0)
                {
                    packed[position >> 3] |= (byte)(0x80 >> (position & 7));
                }
            }
        }

        return packed;
    }

    private List<byte[]> EncodeBlocks()
    {
        var (blockWidth, blockHeight) = Tile ?? (Width, GetRowsPerStrip());
        var across = (Width + blockWidth - 1) / blockWidth;
        var down = (Height + blockHeight - 1) / blockHeight;
        var blocks = new List<byte[]>();
        for (var by = 0; by < down; by++)
        {
            for (var bx = 0; bx < across; bx++)
            {
                // A tile is stored whole (padded with zero samples); the last strip only has the rows of the image
                var rows = Tile is null ? Math.Min(blockHeight, Height - (by * blockHeight)) : blockHeight;
                using var block = new MemoryStream();
                for (var y = 0; y < rows; y++)
                {
                    var row = new double[blockWidth * SamplesPerPixel];
                    var imageY = (by * blockHeight) + y;
                    for (var x = 0; x < blockWidth; x++)
                    {
                        var imageX = (bx * blockWidth) + x;
                        if (imageX >= Width || imageY >= Height)
                            continue;

                        for (var s = 0; s < SamplesPerPixel; s++)
                        {
                            row[(x * SamplesPerPixel) + s] = Samples[(((imageY * Width) + imageX) * SamplesPerPixel) + s];
                        }
                    }

                    var encoded = EncodeRow(row);
                    ApplyPredictor(encoded, blockWidth);
                    block.Write(encoded);
                }

                blocks.Add(Compression == 8 ? Deflate(block.ToArray()) : block.ToArray());
            }
        }

        return blocks;
    }

    private void ApplyPredictor(byte[] row, int width)
    {
        switch (Predictor)
        {
            case 1:
                return;

            case 2 or 34892 or 34893:
                {
                    var distance = (Predictor == 2 ? 1 : Predictor == 34892 ? 2 : 4) * SamplesPerPixel;
                    if (BitsPerSample == 8)
                    {
                        for (var i = row.Length - 1; i >= distance; i--)
                        {
                            row[i] = (byte)(row[i] - row[i - distance]);
                        }
                    }
                    else
                    {
                        var count = row.Length / 2;
                        for (var i = count - 1; i >= distance; i--)
                        {
                            var current = IsBigEndian ? BinaryPrimitives.ReadUInt16BigEndian(row.AsSpan(i * 2)) : BinaryPrimitives.ReadUInt16LittleEndian(row.AsSpan(i * 2));
                            var left = IsBigEndian ? BinaryPrimitives.ReadUInt16BigEndian(row.AsSpan((i - distance) * 2)) : BinaryPrimitives.ReadUInt16LittleEndian(row.AsSpan((i - distance) * 2));
                            var difference = (ushort)(current - left);
                            if (IsBigEndian)
                            {
                                BinaryPrimitives.WriteUInt16BigEndian(row.AsSpan(i * 2), difference);
                            }
                            else
                            {
                                BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(i * 2), difference);
                            }
                        }
                    }

                    return;
                }

            case 3 or 34894 or 34895:
                {
                    // TIFF Technical Note 3: the bytes of the samples in planes, most significant first, then differenced
                    var bytesPerSample = BitsPerSample / 8;
                    var count = width * SamplesPerPixel;
                    var planes = new byte[row.Length];
                    for (var i = 0; i < count; i++)
                    {
                        for (var b = 0; b < bytesPerSample; b++)
                        {
                            var position = IsBigEndian ? b : bytesPerSample - 1 - b;
                            planes[(b * count) + i] = row[(i * bytesPerSample) + position];
                        }
                    }

                    var distance = (Predictor == 3 ? 1 : Predictor == 34894 ? 2 : 4) * SamplesPerPixel;
                    for (var i = planes.Length - 1; i >= distance; i--)
                    {
                        planes[i] = (byte)(planes[i] - planes[i - distance]);
                    }

                    planes.CopyTo(row, 0);
                    return;
                }

            default:
                throw new InvalidOperationException("Unknown predictor.");
        }
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    private static void AddAscii(List<DngTag> tags, ushort tag, string? value)
    {
        if (value is not null)
        {
            tags.Add(DngTag.Ascii(tag, value));
        }
    }

    private static void AddRationals(List<DngTag> tags, ushort tag, double[]? values)
    {
        if (values is not null)
        {
            tags.Add(DngTag.Rationals(tag, values));
        }
    }

    private static void AddSignedRationals(List<DngTag> tags, ushort tag, double[]? values)
    {
        if (values is not null)
        {
            tags.Add(DngTag.SignedRationals(tag, values));
        }
    }

    private static void AddUndefined(List<DngTag> tags, ushort tag, byte[]? values)
    {
        if (values is not null)
        {
            tags.Add(new DngTag(tag, 7, values.Length, values));
        }
    }

    /// <summary>A minimal writer of classic TIFF files: blocks of bytes, then directories whose values follow each directory.</summary>
    private sealed class TiffWriter(bool bigEndian) : IDisposable
    {
        private readonly MemoryStream _data = new();
        private long _end;

        public void Dispose() => _data.Dispose();

        public long Position
        {
            get => _data.Position;
            set
            {
                if (_data.Length < value)
                {
                    _data.SetLength(value);
                }

                _data.Position = value;
            }
        }

        public uint Append(byte[] bytes)
        {
            Align();
            var offset = (uint)_data.Position;
            _data.Write(bytes);
            _end = _data.Position;
            return offset;
        }

        public uint WriteDirectory(List<DngTag> tags, uint next)
        {
            Align();
            tags.Sort((a, b) => a.Tag.CompareTo(b.Tag));
            var offset = (uint)_data.Position;
            var valuesOffset = offset + 2 + (tags.Count * 12) + 4;
            var header = new byte[2 + (tags.Count * 12) + 4];
            using var values = new MemoryStream();
            var lastPadded = false;
            WriteUInt16(header, 0, (ushort)tags.Count);
            // The values are written in the reverse order of the tags: the last bytes of the file are then the values of
            // the first tags (Make, the sample layout), which every reader needs, so that no proper prefix is a valid file
            for (var i = tags.Count - 1; i >= 0; i--)
            {
                var tag = tags[i];
                var bytes = tag.Encode(bigEndian);
                var entry = 2 + (i * 12);
                WriteUInt16(header, entry, tag.Tag);
                WriteUInt16(header, entry + 2, tag.Type);
                WriteUInt32(header, entry + 4, (uint)tag.Count);
                if (bytes.Length <= 4)
                {
                    bytes.CopyTo(header, entry + 8);
                }
                else
                {
                    WriteUInt32(header, entry + 8, (uint)(valuesOffset + values.Length));
                    values.Write(bytes);
                    lastPadded = values.Length % 2 == 1;
                    if (lastPadded)
                    {
                        values.WriteByte(0);
                    }
                }
            }

            WriteUInt32(header, 2 + (tags.Count * 12), next);
            _data.Write(header);
            var valueBytes = values.ToArray();
            _data.Write(valueBytes);

            // The padding of the last value is not part of the directory
            _end = _data.Position - (valueBytes.Length > 0 && lastPadded ? 1 : 0);
            return offset;
        }

        public byte[] ToArray(uint firstDirectory)
        {
            // Every byte of the file is used: no padding after the last value
            var result = _data.ToArray()[..(int)_end];
            (bigEndian ? "MM\0*"u8 : "II*\0"u8).CopyTo(result);
            WriteUInt32(result, 4, firstDirectory);
            return result;
        }

        private void Align()
        {
            if (_data.Position % 2 == 1)
            {
                _data.WriteByte(0);
            }
        }

        private void WriteUInt16(byte[] data, int offset, ushort value)
        {
            if (bigEndian)
            {
                BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset), value);
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), value);
            }
        }

        private void WriteUInt32(byte[] data, int offset, uint value)
        {
            if (bigEndian)
            {
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), value);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
            }
        }
    }
}
