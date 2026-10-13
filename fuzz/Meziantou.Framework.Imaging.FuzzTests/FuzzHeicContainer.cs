using System.Buffers.Binary;
using Meziantou.Framework.Imaging.TestHarness.Heif;
using Meziantou.Framework.Imaging.TestHarness.Hevc;

namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>
/// The HEVC side of the HEIF family for the fuzz tests: a minimal HEIC file around a raw HEVC byte stream of the corpus (a
/// still <c>hvc1</c> item for a stream of one access unit, an image sequence of the brand <c>hevc</c> otherwise), so that
/// the HEVC vectors are fuzz seeds of the public API, and the location of the NAL units and of the boxes of a file, so that
/// mutations change the slice data, the parameter sets or a table of the container without breaking what locates them.
/// The files are built with the box writer of the test harness, not with the encoder of the library.
/// </summary>
/// <remarks>
/// A sequence is one <c>pict</c> track with one sample per access unit, in decoding order, without composition offsets:
/// the output order of the pictures is the one the decoder derives from their picture order counts. The parameter sets
/// that precede the first slice segment are in the <c>hvcC</c> record; every NAL unit of the stream stays in its sample,
/// so that the streams whose parameter sets change keep them.
/// </remarks>
internal static class FuzzHeicContainer
{
    /// <summary>How a stream is wrapped.</summary>
    /// <param name="NalLengthSize">The number of bytes of the length of a NAL unit: 1, 2 or 4.</param>
    /// <param name="ParameterSetsInRecord">Whether the record has the parameter sets that precede the first slice segment.</param>
    /// <param name="ParameterSetsInSamples">Whether the samples keep the parameter sets of the stream.</param>
    /// <param name="SampleEntryType"><c>hvc1</c> or <c>hev1</c>.</param>
    /// <param name="ForceSequence">Whether a stream of one access unit is a sequence too.</param>
    /// <param name="CompositionOffsets">The composition offsets of the samples, or <see langword="null"/> for none.</param>
    /// <param name="AlphaUnits">The NAL units of the stream of an alpha plane (an auxiliary item or an auxiliary track), or <see langword="null"/> for none.</param>
    internal sealed record Options(int NalLengthSize = 4, bool ParameterSetsInRecord = true, bool ParameterSetsInSamples = true, string SampleEntryType = "hvc1", bool ForceSequence = false, int[]? CompositionOffsets = null, IReadOnlyList<ReadOnlyMemory<byte>>? AlphaUnits = null);

    /// <summary>A NAL unit of a file: the position of its length, the number of bytes of the length, and the length.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    internal readonly record struct NalUnit(int LengthOffset, int LengthSize, int Length)
    {
        public int Offset => LengthOffset + LengthSize;
    }

    /// <summary>Wraps a raw HEVC byte stream (Annex B) into a HEIC file; <see langword="null"/> when the stream has no picture.</summary>
    public static byte[]? Wrap(ReadOnlyMemory<byte> annexB, Options? options = null)
    {
        options ??= new Options();
        IReadOnlyList<ReadOnlyMemory<byte>> units;
        try
        {
            units = ReferenceHevcStream.SplitAnnexB(annexB);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        return Wrap(units, options);
    }

    /// <summary>Wraps the NAL units of a stream into a HEIC file; <see langword="null"/> when they have no picture or do not fit the options.</summary>
    public static byte[]? Wrap(IReadOnlyList<ReadOnlyMemory<byte>> units, Options options)
    {
        if (Describe(units, options) is not { } color)
            return null;

        var alpha = options.AlphaUnits is null ? null : Describe(options.AlphaUnits, options);
        if (color.Samples.Count is 1 && !options.ForceSequence)
        {
            var still = new HeifFileBuilder { PrimaryItemId = 1, MajorBrand = "heic" };
            SetBrands(still, "mif1", "heic");
            still.AddItem(1, "hvc1", color.Samples[0], still.AddProperty(HeifFileBuilder.ImageSize((uint)color.Width, (uint)color.Height)), -still.AddProperty(color.Configuration));
            if (alpha is not null)
            {
                // An auxiliary image of the alpha type, which the primary item refers to
                still.AddItem(2, "hvc1", alpha.Samples[0],
                    still.AddProperty(HeifFileBuilder.ImageSize((uint)alpha.Width, (uint)alpha.Height)),
                    -still.AddProperty(alpha.Configuration),
                    still.AddProperty(HeifFileBuilder.AuxiliaryType(ReferenceHeif.HevcAlphaAuxiliaryType)));
                still.References.Add(("auxl", 2, [1]));
            }

            return still.Build();
        }

        var builder = new HeifFileBuilder { MajorBrand = "hevc", MovieTimescale = 1000 };
        SetBrands(builder, "hevc", "msf1");
        builder.Layout.Clear();
        builder.Layout.Add(HeifFileBuilder.FileTypeBox);
        builder.Layout.Add(HeifFileBuilder.MovieBox);
        builder.Layout.Add(HeifFileBuilder.MediaDataBox);
        var track = new HeifFileBuilder.Track(1, "pict", color.Width, color.Height) { Timescale = 1000, Duration = (ulong)color.Samples.Count * 40, SampleEntryType = options.SampleEntryType };
        track.SampleEntryBoxes.Add(color.Configuration);
        foreach (var sample in color.Samples)
        {
            track.Samples.Add((sample, 40));
        }

        if (options.CompositionOffsets is { } offsets)
        {
            track.CompositionOffsets = [.. Enumerable.Range(0, color.Samples.Count).Select(index => offsets[index % offsets.Length])];
        }

        builder.Tracks.Add(track);
        if (alpha is not null)
        {
            // An auxiliary track of the alpha type, which refers to the track of the color
            var alphaTrack = new HeifFileBuilder.Track(2, "auxv", alpha.Width, alpha.Height) { Timescale = 1000, Duration = (ulong)alpha.Samples.Count * 40, SampleEntryType = options.SampleEntryType };
            alphaTrack.SampleEntryBoxes.Add(alpha.Configuration);
            alphaTrack.SampleEntryBoxes.Add(HeifFileBuilder.FullBox("auxi", 0, 0, Encoding.ASCII.GetBytes(ReferenceHeif.HevcAlphaAuxiliaryType), [0]));
            alphaTrack.References.Add(("auxl", [1]));
            foreach (var sample in alpha.Samples)
            {
                alphaTrack.Samples.Add((sample, 40));
            }

            builder.Tracks.Add(alphaTrack);
        }

        return builder.Build();
    }

    // The configuration record and the samples of a stream
    private static StreamDescription? Describe(IReadOnlyList<ReadOnlyMemory<byte>> units, Options options)
    {
        units = [.. units.Where(unit => unit.Length >= 2)];
        ReferenceHevcSequence? sequence = null;
        var arrays = new List<(int NalUnitType, List<byte[]> NalUnits)>();
        foreach (var unit in units)
        {
            var type = ReferenceHevcStream.GetNalUnitType(unit.Span);
            if (type < 32)
                break;

            if (type is ReferenceHevcStream.NalVps or ReferenceHevcStream.NalSps or ReferenceHevcStream.NalPps)
            {
                var index = arrays.FindIndex(array => array.NalUnitType == type);
                if (index < 0)
                {
                    arrays.Add((type, []));
                    index = arrays.Count - 1;
                }

                if (unit.Length <= ushort.MaxValue && arrays[index].NalUnits.Count < 64)
                {
                    arrays[index].NalUnits.Add(unit.ToArray());
                }
            }
        }

        foreach (var unit in units)
        {
            if (ReferenceHevcStream.GetNalUnitType(unit.Span) is not ReferenceHevcStream.NalSps)
                continue;

            try
            {
                sequence = ReferenceHevcStream.ReadSequenceParameterSet(unit.Span);
                break;
            }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentException or IndexOutOfRangeException or OverflowException)
            {
                // A mutated stream: the sizes of the container are then arbitrary
            }
        }

        var chromaFormat = sequence?.ChromaFormatIdc ?? 1;
        var bitDepth = Math.Clamp(sequence?.BitDepthLuma ?? 8, 8, 15);
        var bitDepthChroma = Math.Clamp(sequence?.BitDepthChroma ?? 8, 8, 15);
        var configuration = HeifFileBuilder.HevcConfiguration(chromaFormat, bitDepth, bitDepthChroma,
            options.ParameterSetsInRecord ? arrays.Select(array => (array.NalUnitType, array.NalUnits.ToArray())) : [], options.NalLengthSize);

        var samples = new List<byte[]>();
        foreach (var accessUnit in GroupAccessUnits(units))
        {
            var kept = options.ParameterSetsInSamples ? accessUnit : [.. accessUnit.Where(unit => ReferenceHevcStream.GetNalUnitType(unit.Span) is not (ReferenceHevcStream.NalVps or ReferenceHevcStream.NalSps or ReferenceHevcStream.NalPps))];
            if (kept.Count is 0 || (options.NalLengthSize < 4 && kept.Any(unit => unit.Length >= 1 << (8 * options.NalLengthSize))))
                continue;

            samples.Add(ReferenceHevcStream.ToLengthPrefixed(kept, options.NalLengthSize));
        }

        return samples.Count is 0 ? null : new StreamDescription(Math.Clamp(sequence?.CropWidth ?? 64, 1, 65535), Math.Clamp(sequence?.CropHeight ?? 64, 1, 65535), configuration, samples);
    }

    private sealed record StreamDescription(int Width, int Height, byte[] Configuration, List<byte[]> Samples);

    /// <summary>
    /// Groups the NAL units of a stream, mutated or not, into access units (Rec. ITU-T H.265, clause 7.4.2.4.4): an access unit
    /// starts with the first slice segment of a picture, or with a NAL unit that can only precede it (a parameter set, an
    /// access unit delimiter, a prefix SEI message) after the slice segments of the picture before.
    /// </summary>
    public static List<IReadOnlyList<ReadOnlyMemory<byte>>> GroupAccessUnits(IReadOnlyList<ReadOnlyMemory<byte>> units)
    {
        var accessUnits = new List<IReadOnlyList<ReadOnlyMemory<byte>>>();
        var current = new List<ReadOnlyMemory<byte>>();
        var hasSlice = false;
        foreach (var unit in units)
        {
            var type = ReferenceHevcStream.GetNalUnitType(unit.Span);
            var startsAccessUnit = type < 32
                ? hasSlice && unit.Length > 2 && (unit.Span[2] & 0x80) is not 0
                : hasSlice && type is (>= 32 and <= 35) or 39 or (>= 41 and <= 44) or (>= 48 and <= 55);
            if (startsAccessUnit)
            {
                accessUnits.Add(current);
                current = [];
                hasSlice = false;
            }

            current.Add(unit);
            hasSlice |= type < 32;
        }

        if (current.Count > 0)
        {
            accessUnits.Add(current);
        }

        return accessUnits;
    }

    /// <summary>Returns whether a file declares a brand of the HEVC image sequences.</summary>
    public static bool IsSequence(ReadOnlySpan<byte> data)
    {
        if (!FuzzAvifContainer.IsIsoBaseMedia(data))
            return false;

        var size = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(data), (uint)data.Length);
        for (var offset = 8; offset + 4 <= size; offset += 4)
        {
            if (offset is not 12 && (data.Slice(offset, 4).SequenceEqual("hevc"u8) || data.Slice(offset, 4).SequenceEqual("hevx"u8) || data.Slice(offset, 4).SequenceEqual("msf1"u8)))
                return true;
        }

        return false;
    }

    /// <summary>Returns whether a file has an HEVC decoder configuration record.</summary>
    public static bool HasHevcConfiguration(ReadOnlySpan<byte> data) => FuzzAvifContainer.IsIsoBaseMedia(data) && data.IndexOf("hvcC"u8) >= 4;

    /// <summary>
    /// Gets the payloads of the boxes of a type, wherever they are: the four characters of the type are searched, and the
    /// size that precedes them must fit the file.
    /// </summary>
    public static List<(int Offset, int Length)> FindBoxes(ReadOnlySpan<byte> data, ReadOnlySpan<byte> type)
    {
        var result = new List<(int Offset, int Length)>();
        var position = 4;
        while (position + 4 <= data.Length)
        {
            var index = data[position..].IndexOf(type);
            if (index < 0)
                break;

            position += index;
            var size = BinaryPrimitives.ReadUInt32BigEndian(data[(position - 4)..]);
            if (size >= 8 && size <= (uint)(data.Length - position + 4))
            {
                result.Add((position + 4, (int)size - 8));
            }

            position += 4;
        }

        return result;
    }

    /// <summary>Gets the number of bytes of the length of a NAL unit that the first configuration record of a file tells: 4 without record.</summary>
    public static int GetNalLengthSize(ReadOnlySpan<byte> data)
    {
        var records = FindBoxes(data, "hvcC"u8);
        if (records.Count is 0 || records[0].Length < 23)
            return 4;

        return (data[records[0].Offset + 21] & 3) switch
        {
            0 => 1,
            1 => 2,
            _ => 4,
        };
    }

    /// <summary>
    /// Gets the NAL units of the media data of a file: the payload of each <c>mdat</c> box is walked from its start as a
    /// sequence of NAL units preceded by their length, for as long as the lengths fit and the headers are those of NAL units
    /// of the base layer. What follows (other items, descriptors of grids, metadata) is left out.
    /// </summary>
    public static List<NalUnit> FindNalUnits(ReadOnlySpan<byte> data)
    {
        var result = new List<NalUnit>();
        var lengthSize = GetNalLengthSize(data);
        foreach (var (offset, length) in FuzzAvifContainer.FindMediaData(data))
        {
            var position = offset;
            var end = offset + length;
            while (position + lengthSize + 2 <= end)
            {
                var nalLength = 0;
                for (var i = 0; i < lengthSize; i++)
                {
                    nalLength = (nalLength << 8) | data[position + i];
                }

                // forbidden_zero_bit is 0, nuh_layer_id is small and nuh_temporal_id_plus1 is not 0
                var header = data.Slice(position + lengthSize, 2);
                if (nalLength < 2 || nalLength > end - position - lengthSize || (header[0] & 0x80) is not 0 || (header[1] & 7) is 0 || (header[0] & 1) is not 0)
                {
                    // Something else: an item that is not coded, or padding. The NAL units are looked for again further on
                    position++;
                    continue;
                }

                result.Add(new NalUnit(position, lengthSize, nalLength));
                position += lengthSize + nalLength;
            }
        }

        return result;
    }

    private static void SetBrands(HeifFileBuilder builder, params string[] compatibleBrands)
    {
        builder.CompatibleBrands.Clear();
        foreach (var brand in compatibleBrands)
        {
            builder.CompatibleBrands.Add(brand);
        }
    }
}
