using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Hevc;

namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>
/// Mutations that know the structure of an HEVC stream in a HEIF file, so that they reach what a blind mutation rarely
/// does: the slice data behind an intact slice segment header (the arithmetic decoding, the prediction, the reference
/// pictures), the parameter sets, the SEI messages and the decoder configuration record behind intact lengths and NAL unit
/// headers, the tables of the container (sample sizes, times, offsets, image sizes, grids), and streams whose NAL units,
/// access units or parameter sets are dropped, duplicated, reordered, taken from another stream or cut at a NAL unit.
/// </summary>
internal static class FuzzHevcMutator
{
    private static readonly uint[] InterestingValues = [0, 1, 2, 3, 4, 7, 8, 15, 16, 17, 31, 32, 63, 64, 65, 127, 128, 255, 256, 1023, 1024, 4095, 4096, 8191, 16384, 32767, 32768, 65535, 65536, 0x7FFFFFFF, 0x80000000, 0xFFFFFFFF];

    private static readonly string[] TableBoxes =
    [
        "stts", "ctts", "stsz", "stz2", "stsc", "stco", "co64", "stss", "elst", "tkhd", "mdhd", "mvhd", "stsd", "ispe", "clap", "pixi", "irot", "imir", "auxC", "auxi", "colr",
        "iloc", "iinf", "infe", "ipma", "pitm", "iref", "idat", "hvcC", "tref", "hdlr", "ftyp", "ccst", "cslg",
    ];

    /// <summary>Mutates a seed of the HEIF family that has HEVC data; <see langword="null"/> when the seed is left to the other mutations.</summary>
    public static byte[]? TryMutate(FuzzRandom random, FuzzSeed seed, IReadOnlyList<FuzzSeed> seeds)
    {
        // A stream of the corpus: its structure is changed before it is wrapped again, so that the container stays the one of
        // the new stream
        if (seed.HevcStream is not null && random.Chance(25) && TryMutateStream(random, seed, seeds) is { } rewrapped)
            return rewrapped;

        if (!FuzzHeicContainer.HasHevcConfiguration(seed.Data))
            return null;

        var choice = random.Next(100);
        if (choice < 50)
            return TryMutateNalUnits(random, seed.Data, seeds);

        if (choice < 70)
            return TryMutateBoxes(random, seed.Data);

        return null;
    }

    /// <summary>Changes NAL units in place: the offsets and the sizes of the container stay those of the file.</summary>
    private static byte[]? TryMutateNalUnits(FuzzRandom random, byte[] seed, IReadOnlyList<FuzzSeed> seeds)
    {
        var units = FuzzHeicContainer.FindNalUnits(seed);
        if (units.Count is 0)
            return null;

        var result = (byte[])seed.Clone();
        var slices = units.Where(unit => GetNalUnitType(result, unit) < 32 && unit.Length > 3).ToList();
        var parameterSets = units.Where(unit => GetNalUnitType(result, unit) is >= 32 and <= 34 && unit.Length > 3).ToList();
        var messages = units.Where(unit => GetNalUnitType(result, unit) is 39 or 40 && unit.Length > 3).ToList();
        var count = 1 + random.Next(random.Chance(70) ? 2 : 6);
        for (var i = 0; i < count; i++)
        {
            switch (random.Next(20))
            {
                case < 8 when slices.Count > 0:
                {
                    // The slice segment data, behind the header of the slice segment
                    var unit = random.Pick(slices);
                    var start = Math.Min(unit.Length - 1, GetSliceDataOffset(result, unit, units));
                    MutateBytes(random, result, unit.Offset + start, unit.Length - start, nearStart: random.Chance(30));
                    break;
                }

                case < 10 when slices.Count > 0:
                {
                    // The slice segment header
                    var unit = random.Pick(slices);
                    MutateBytes(random, result, unit.Offset + 2, Math.Min(unit.Length - 2, 2 + random.Next(12)), nearStart: false);
                    break;
                }

                case < 12 when parameterSets.Count > 0:
                {
                    var unit = random.Pick(parameterSets);
                    MutateBytes(random, result, unit.Offset + 2, unit.Length - 2, nearStart: false);
                    break;
                }

                case 12 when messages.Count > 0:
                {
                    var unit = random.Pick(messages);
                    MutateBytes(random, result, unit.Offset + 2, unit.Length - 2, nearStart: random.Chance(60));
                    break;
                }

                case 13:
                {
                    // Another NAL unit type: a slice segment of another kind of picture, an end of sequence, a reserved type
                    var unit = random.Pick(units);
                    ReadOnlySpan<byte> types = [0, 1, 8, 9, 6, 7, 16, 17, 18, 19, 20, 21, 22, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 48, 63];
                    result[unit.Offset] = (byte)((result[unit.Offset] & 0x81) | (types[random.Next(types.Length)] << 1));
                    break;
                }

                case 14:
                {
                    // The layer and the temporal sub-layer of a NAL unit
                    var unit = random.Pick(units);
                    result[unit.Offset + 1] = random.Chance(50) ? (byte)(1 + random.Next(7)) : random.NextByte();
                    break;
                }

                case 15 when units.Count > 1:
                {
                    // Two NAL units that follow each other change places
                    var index = random.Next(units.Count - 1);
                    var first = units[index];
                    var second = units[index + 1];
                    if (second.LengthOffset != first.Offset + first.Length)
                        break;

                    var a = result.AsSpan(first.LengthOffset, first.LengthSize + first.Length).ToArray();
                    var b = result.AsSpan(second.LengthOffset, second.LengthSize + second.Length).ToArray();
                    b.CopyTo(result.AsSpan(first.LengthOffset));
                    a.CopyTo(result.AsSpan(first.LengthOffset + b.Length));
                    units[index] = new FuzzHeicContainer.NalUnit(first.LengthOffset, second.LengthSize, second.Length);
                    units[index + 1] = new FuzzHeicContainer.NalUnit(first.LengthOffset + b.Length, first.LengthSize, first.Length);
                    break;
                }

                case 16:
                {
                    // A NAL unit takes the place of another one: a duplicate, or a parameter set or a slice segment of
                    // another stream. What is left of the place is filler data
                    var target = random.Pick(units);
                    byte[] source;
                    if (random.Chance(50))
                    {
                        var other = random.Pick(units);
                        source = seed.AsSpan(other.Offset, other.Length).ToArray();
                    }
                    else
                    {
                        var otherSeed = random.Pick(seeds).Data;
                        var otherUnits = FuzzHeicContainer.FindNalUnits(otherSeed);
                        if (otherUnits.Count is 0)
                            break;

                        var wanted = random.Chance(70) ? otherUnits.Where(unit => GetNalUnitType(otherSeed, unit) is >= 32 and <= 34).ToList() : otherUnits;
                        if (wanted.Count is 0)
                            break;

                        var other = random.Pick(wanted);
                        source = otherSeed.AsSpan(other.Offset, other.Length).ToArray();
                    }

                    var left = target.Length - source.Length;
                    if (left is not 0 && left < target.LengthSize + 2)
                        break;

                    WriteLength(result, target.LengthOffset, target.LengthSize, source.Length);
                    source.CopyTo(result.AsSpan(target.Offset));
                    if (left > 0)
                    {
                        var filler = target.Offset + source.Length;
                        WriteLength(result, filler, target.LengthSize, left - target.LengthSize);
                        result.AsSpan(filler + target.LengthSize, left - target.LengthSize).Fill(0xFF);
                        result[filler + target.LengthSize] = 38 << 1;
                        result[filler + target.LengthSize + 1] = 1;
                    }

                    break;
                }

                case 17:
                {
                    // The length of a NAL unit
                    var unit = random.Pick(units);
                    var value = random.Next(4) switch
                    {
                        0 => unit.Length - 1,
                        1 => unit.Length + 1,
                        2 => (int)Math.Min(random.Pick(InterestingValues), int.MaxValue),
                        _ => random.Next(Math.Max(1, unit.Length * 2)),
                    };
                    WriteLength(result, unit.LengthOffset, unit.LengthSize, value);
                    break;
                }

                case 18:
                {
                    // The file ends at a NAL unit
                    var unit = random.Pick(units);
                    return result.AsSpan(0, random.Chance(50) ? unit.LengthOffset : unit.Offset + Math.Min(unit.Length, 2)).ToArray();
                }

                default:
                {
                    // The decoder configuration record: its fields, the counts and the lengths of its arrays, its parameter sets
                    var records = FuzzHeicContainer.FindBoxes(result, "hvcC"u8);
                    if (records.Count is 0)
                        break;

                    var (offset, length) = random.Pick(records);
                    if (length is 0)
                        break;

                    if (random.Chance(40))
                    {
                        MutateBytes(random, result, offset, Math.Min(length, 23), nearStart: false);
                    }
                    else if (length > 26 && random.Chance(50))
                    {
                        // A 16-bit count or length
                        var position = offset + 22 + random.Next(Math.Min(length - 24, 24));
                        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(position), (ushort)random.Pick(InterestingValues));
                    }
                    else
                    {
                        MutateBytes(random, result, offset, length, nearStart: false);
                    }

                    break;
                }
            }
        }

        return result;
    }

    /// <summary>Changes a field of a table of the container, in place.</summary>
    private static byte[]? TryMutateBoxes(FuzzRandom random, byte[] seed)
    {
        var result = (byte[])seed.Clone();
        var count = 1 + random.Next(3);
        var changed = false;
        for (var i = 0; i < count; i++)
        {
            var boxes = FuzzHeicContainer.FindBoxes(result, Encoding.ASCII.GetBytes(random.Pick(TableBoxes)));
            if (boxes.Count is 0)
                continue;

            var (offset, length) = random.Pick(boxes);
            if (length <= 0)
                continue;

            changed = true;
            switch (random.Next(6))
            {
                case 0 or 1 when length >= 4:
                {
                    // A 32-bit field: an interesting value, or its value more or less a little
                    var position = offset + (random.Next(length / 4) * 4);
                    var value = BinaryPrimitives.ReadUInt32BigEndian(result.AsSpan(position));
                    value = random.Next(4) switch
                    {
                        0 => value + 1,
                        1 => value - 1,
                        2 => value * 2,
                        _ => random.Pick(InterestingValues),
                    };
                    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(position), value);
                    break;
                }

                case 2 when length >= 2:
                {
                    var position = offset + random.Next(length - 1);
                    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(position), (ushort)random.Pick(InterestingValues));
                    break;
                }

                case 3 when length >= 8:
                {
                    // Two entries of a table change places
                    var a = offset + (random.Next(length / 4) * 4);
                    var b = offset + (random.Next(length / 4) * 4);
                    var first = BinaryPrimitives.ReadUInt32BigEndian(result.AsSpan(a));
                    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(a), BinaryPrimitives.ReadUInt32BigEndian(result.AsSpan(b)));
                    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(b), first);
                    break;
                }

                default:
                    MutateBytes(random, result, offset, length, nearStart: random.Chance(50));
                    break;
            }
        }

        return changed ? result : null;
    }

    /// <summary>Changes the structure of the stream of a seed, and wraps the new stream.</summary>
    private static byte[]? TryMutateStream(FuzzRandom random, FuzzSeed seed, IReadOnlyList<FuzzSeed> seeds)
    {
        List<ReadOnlyMemory<byte>> units;
        try
        {
            units = [.. ReferenceHevcStream.SplitAnnexB(seed.HevcStream)];
        }
        catch (InvalidDataException)
        {
            return null;
        }

        var count = 1 + random.Next(random.Chance(70) ? 2 : 5);
        for (var i = 0; i < count && units.Count > 0; i++)
        {
            switch (random.Next(14))
            {
                case 0:
                    units.RemoveAt(random.Next(units.Count));
                    break;

                case 1:
                    units.Insert(random.Next(units.Count + 1), random.Pick(units));
                    break;

                case 2:
                {
                    var a = random.Next(units.Count);
                    var b = random.Next(units.Count);
                    (units[a], units[b]) = (units[b], units[a]);
                    break;
                }

                case 3:
                {
                    // Access units are dropped, duplicated or change places
                    var accessUnits = FuzzHeicContainer.GroupAccessUnits([.. units.Where(unit => unit.Length >= 2)]).Select(accessUnit => accessUnit.ToList()).ToList();
                    if (accessUnits.Count < 2)
                        break;

                    var a = random.Next(accessUnits.Count);
                    var b = random.Next(accessUnits.Count);
                    switch (random.Next(3))
                    {
                        case 0:
                            accessUnits.RemoveAt(a);
                            break;
                        case 1:
                            accessUnits.Insert(b, accessUnits[a]);
                            break;
                        default:
                            (accessUnits[a], accessUnits[b]) = (accessUnits[b], accessUnits[a]);
                            break;
                    }

                    units = [.. accessUnits.SelectMany(accessUnit => accessUnit)];
                    break;
                }

                case 4:
                    // The stream ends at a NAL unit
                    units = [.. units.Take(1 + random.Next(units.Count))];
                    break;

                case 5 or 6:
                {
                    // The parameter sets of another stream, in place of those of the stream or beside them
                    var other = random.Pick(seeds).HevcStream;
                    if (other is null)
                        break;

                    var otherSets = ReferenceHevcStream.SplitAnnexB(other).Where(unit => ReferenceHevcStream.GetNalUnitType(unit.Span) is >= 32 and <= 34).ToList();
                    if (otherSets.Count is 0)
                        break;

                    var set = random.Pick(otherSets);
                    var type = ReferenceHevcStream.GetNalUnitType(set.Span);
                    var same = Enumerable.Range(0, units.Count).Where(index => units[index].Length >= 2 && ReferenceHevcStream.GetNalUnitType(units[index].Span) == type).ToList();
                    if (same.Count > 0 && random.Chance(60))
                    {
                        units[random.Pick(same)] = set;
                    }
                    else
                    {
                        units.Insert(random.Next(units.Count + 1), set);
                    }

                    break;
                }

                case 7:
                {
                    // Another stream follows the stream, or is in the middle of it: a coded video sequence of another size,
                    // of another format, with or without an end of sequence NAL unit before it
                    var other = random.Pick(seeds).HevcStream;
                    if (other is null)
                        break;

                    var otherUnits = ReferenceHevcStream.SplitAnnexB(other);
                    var position = random.Chance(60) ? units.Count : random.Next(units.Count + 1);
                    var inserted = otherUnits.Take(1 + random.Next(Math.Min(otherUnits.Count, 40))).ToList();
                    if (random.Chance(40))
                    {
                        inserted.Insert(0, new byte[] { (byte)((random.Chance(70) ? 36 : 37) << 1), 1 });
                    }

                    units.InsertRange(position, inserted);
                    break;
                }

                case 8:
                    // An end of sequence, an end of bitstream or an access unit delimiter NAL unit
                    units.Insert(random.Next(units.Count + 1), new byte[] { (byte)(random.Pick<int>([35, 36, 36, 37]) << 1), 1, 0x50 });
                    break;

                case 9:
                {
                    // Another NAL unit type
                    var index = random.Next(units.Count);
                    var copy = units[index].ToArray();
                    if (copy.Length < 2)
                        break;

                    ReadOnlySpan<byte> types = [0, 1, 8, 9, 6, 7, 16, 17, 18, 19, 20, 21, 32, 33, 34, 36, 39, 40];
                    copy[0] = (byte)((copy[0] & 0x81) | (types[random.Next(types.Length)] << 1));
                    units[index] = copy;
                    break;
                }

                default:
                {
                    // Bytes of a NAL unit are changed, inserted or removed: the lengths follow
                    var index = random.Next(units.Count);
                    var bytes = new List<byte>(units[index].ToArray());
                    if (bytes.Count < 3)
                        break;

                    var position = 2 + random.Next(bytes.Count - 2);
                    switch (random.Next(5))
                    {
                        case 0:
                            bytes.RemoveRange(position, Math.Min(bytes.Count - position, 1 + random.Next(4)));
                            break;
                        case 1:
                            bytes.InsertRange(position, Enumerable.Range(0, 1 + random.Next(4)).Select(_ => random.NextByte()));
                            break;
                        case 2:
                            bytes.RemoveRange(position, bytes.Count - position);
                            break;
                        default:
                            bytes[position] ^= (byte)(1 << random.Next(8));
                            break;
                    }

                    units[index] = bytes.ToArray();
                    break;
                }
            }
        }

        var options = new FuzzHeicContainer.Options(
            NalLengthSize: random.Chance(80) ? 4 : random.Chance(50) ? 2 : 1,
            ParameterSetsInRecord: random.Chance(85),
            ParameterSetsInSamples: random.Chance(85),
            SampleEntryType: random.Chance(70) ? "hvc1" : "hev1",
            ForceSequence: random.Chance(15),
            CompositionOffsets: random.Chance(20) ? [.. Enumerable.Range(0, 1 + random.Next(6)).Select(_ => random.Chance(70) ? random.Next(8) * 40 : unchecked((int)random.Pick(InterestingValues)))] : null,
            AlphaUnits: random.Chance(20) ? GetAlphaUnits(random, seed, seeds) : null);
        return FuzzHeicContainer.Wrap(units, options);
    }

    // The stream of an alpha plane: the stream of the seed itself (the same size, as many pictures), or the stream of
    // another seed (another size, another format, another number of pictures), whole or cut
    private static List<ReadOnlyMemory<byte>>? GetAlphaUnits(FuzzRandom random, FuzzSeed seed, IReadOnlyList<FuzzSeed> seeds)
    {
        var stream = random.Chance(60) ? seed.HevcStream : random.Pick(seeds).HevcStream;
        if (stream is null)
            return null;

        List<ReadOnlyMemory<byte>> units = [.. ReferenceHevcStream.SplitAnnexB(stream)];
        return random.Chance(25) ? [.. units.Take(1 + random.Next(units.Count))] : units;
    }

    private static int GetNalUnitType(byte[] data, FuzzHeicContainer.NalUnit unit) => (data[unit.Offset] >> 1) & 0x3F;

    private static void WriteLength(byte[] data, int offset, int size, int value)
    {
        for (var i = 0; i < size; i++)
        {
            data[offset + i] = (byte)(value >> (8 * (size - 1 - i)));
        }
    }

    private static void MutateBytes(FuzzRandom random, byte[] data, int offset, int length, bool nearStart)
    {
        if (length <= 0)
            return;

        var position = offset + random.Next(nearStart ? Math.Min(length, 24) : length);
        switch (random.Next(6))
        {
            case 0 or 1 or 2:
                data[position] ^= (byte)(1 << random.Next(8));
                break;
            case 3:
                data[position] = random.NextByte();
                break;
            case 4:
                data[position] = (byte)random.Pick(InterestingValues);
                break;
            default:
            {
                var run = Math.Min(offset + length - position, 1 + random.Next(8));
                data.AsSpan(position, run).Fill(random.Chance(50) ? (byte)0 : random.Chance(50) ? (byte)0xFF : random.NextByte());
                break;
            }
        }
    }

    /// <summary>
    /// Gets the position of the slice segment data in a slice segment NAL unit: where the library, with the parameter sets
    /// of the file, finds the end of the slice segment header; or a guess when it cannot tell.
    /// </summary>
    private static int GetSliceDataOffset(byte[] data, FuzzHeicContainer.NalUnit unit, List<FuzzHeicContainer.NalUnit> units)
    {
        var guess = Math.Min(unit.Length - 1, 2 + Math.Min(12, unit.Length / 4));
        try
        {
            var nalUnit = data.AsSpan(unit.Offset, unit.Length);
            var header = HevcNalUnitHeader.Read(nalUnit);
            var rbsp = ReferenceHevcStream.GetRbsp(nalUnit);
            var ppsId = HevcSliceSegmentHeader.ReadPicParameterSetId(rbsp, header, out _);
            HevcPictureParameterSet? pps = null;
            var sequenceParameterSets = new List<HevcSequenceParameterSet>();
            foreach (var candidate in EnumerateParameterSets(data, units))
            {
                var type = (candidate.Span[0] >> 1) & 0x3F;
                if (type is 33)
                {
                    sequenceParameterSets.Add(HevcSequenceParameterSet.ParseRbsp(ReferenceHevcStream.GetRbsp(candidate.Span)));
                }
                else if (type is 34 && pps is null)
                {
                    var parsed = HevcPictureParameterSet.ParseRbsp(ReferenceHevcStream.GetRbsp(candidate.Span));
                    pps = parsed.PicParameterSetId == ppsId ? parsed : null;
                }
            }

            var sps = pps is null ? null : sequenceParameterSets.FirstOrDefault(item => item.SeqParameterSetId == pps.SeqParameterSetId);
            if (pps is null || sps is null)
                return guess;

            var scope = AllocationScope.Create(FuzzOracle.Configuration, nameof(FuzzHevcMutator));
            using var layout = HevcPictureLayout.Create(scope, sps, pps);
            var dataOffset = HevcSliceSegmentHeader.Parse(rbsp, header, layout, sliceHeader: null).DataOffset;

            // The position in the NAL unit of a position in its payload without the emulation prevention bytes
            var zeros = 0;
            var payloadIndex = 0;
            for (var i = 2; i < nalUnit.Length; i++)
            {
                if (zeros >= 2 && nalUnit[i] is 3)
                {
                    zeros = 0;
                    continue;
                }

                if (payloadIndex == dataOffset)
                    return i;

                payloadIndex++;
                zeros = nalUnit[i] is 0 ? zeros + 1 : 0;
            }

            return guess;
        }
        catch (Exception exception) when (exception is ImageException or ArgumentException or IndexOutOfRangeException or InvalidDataException)
        {
            return guess;
        }
    }

    // The parameter sets of a file: those of its decoder configuration records, then those of its media data
    private static IEnumerable<ReadOnlyMemory<byte>> EnumerateParameterSets(byte[] data, List<FuzzHeicContainer.NalUnit> units)
    {
        foreach (var (offset, length) in FuzzHeicContainer.FindBoxes(data, "hvcC"u8))
        {
            if (length < 23)
                continue;

            var position = offset + 23;
            var end = offset + length;
            for (int array = 0, arrayCount = data[offset + 22]; array < arrayCount && position + 3 <= end; array++)
            {
                var count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(position + 1));
                position += 3;
                for (var i = 0; i < count && position + 2 <= end; i++)
                {
                    var nalLength = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(position));
                    position += 2;
                    if (nalLength < 2 || nalLength > end - position)
                        yield break;

                    yield return data.AsMemory(position, nalLength);
                    position += nalLength;
                }
            }
        }

        foreach (var unit in units)
        {
            if (((data[unit.Offset] >> 1) & 0x3F) is 33 or 34)
                yield return data.AsMemory(unit.Offset, unit.Length);
        }
    }
}
