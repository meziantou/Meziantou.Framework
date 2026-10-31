namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>
/// Mutations that know the open bitstream units (OBUs) of an AV1 stream, so that they reach what a blind mutation of the
/// media data rarely does: streams whose units are dropped, duplicated, reordered, cut, given another type or another layer,
/// or taken from another stream, with consistent size fields, wrapped again into an AVIF file. They exercise the order of the
/// units the decoder accepts (temporal delimiters, sequence headers, frame headers, redundant frame headers, tile groups,
/// frames, tile lists, metadata and padding) behind an intact container.
/// </summary>
internal static class FuzzAv1Mutator
{
    private const int TemporalDelimiter = 2;
    private const int Padding = 15;

    // Reserved types included: 0, 9 to 14
    private static readonly int[] Types = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 14, 15];

    // Padding, a temporal delimiter, a reserved type and metadata
    private static readonly int[] InsertedTypes = [Padding, TemporalDelimiter, 9, 5];

    /// <summary>Mutates a seed made of an AV1 stream of the corpus; <see langword="null"/> when the seed is left to the other mutations.</summary>
    public static byte[]? TryMutate(FuzzRandom random, FuzzSeed seed, IReadOnlyList<FuzzSeed> seeds)
    {
        if (seed.Av1Stream is not { } stream || !random.Chance(35) || Split(stream.Data) is not { Count: > 0 } units)
            return null;

        var count = 1 + random.Next(random.Chance(70) ? 2 : 5);
        for (var i = 0; i < count && units.Count > 0; i++)
        {
            switch (random.Next(13))
            {
                case 0:
                    units.RemoveAt(random.Next(units.Count));
                    break;

                case 1:
                {
                    // A second copy, often right after the first one: a repeated sequence header, frame header or tile group
                    var index = random.Next(units.Count);
                    units.Insert(random.Chance(50) ? index + 1 : random.Next(units.Count + 1), units[index]);
                    break;
                }

                case 2:
                {
                    var index = random.Next(units.Count);
                    var unit = units[index];
                    units.RemoveAt(index);
                    units.Insert(random.Next(units.Count + 1), unit);
                    break;
                }

                case 3 when units.Count > 1:
                {
                    var index = random.Next(units.Count - 1);
                    (units[index], units[index + 1]) = (units[index + 1], units[index]);
                    break;
                }

                case 4:
                {
                    // A unit of another stream: its sequence header, a frame of another size or another bit depth
                    var other = random.Pick(seeds);
                    if (other.Av1Stream is not null && Split(other.Av1Stream.Data) is { Count: > 0 } otherUnits)
                    {
                        units.Insert(random.Next(units.Count + 1), random.Pick(otherUnits));
                    }

                    break;
                }

                case 5:
                {
                    var index = random.Next(units.Count);
                    units[index] = units[index] with { Type = random.Pick(Types) };
                    break;
                }

                case 6:
                {
                    // The extension: removed, or added with any temporal and spatial layer
                    var index = random.Next(units.Count);
                    units[index] = units[index] with { Extension = units[index].Extension is null ? random.NextByte() : null };
                    break;
                }

                case 7 or 8:
                {
                    // The payload, often near its start, where the headers are
                    var index = random.Next(units.Count);
                    var payload = units[index].Payload.ToArray();
                    if (payload.Length > 0)
                    {
                        var position = random.Next(random.Chance(60) ? Math.Min(payload.Length, 16) : payload.Length);
                        payload[position] = random.Chance(70) ? (byte)(payload[position] ^ (1 << random.Next(8))) : random.NextByte();
                        units[index] = units[index] with { Payload = payload };
                    }

                    break;
                }

                case 9:
                {
                    // A payload cut short or empty, with a size field that says so
                    var index = random.Next(units.Count);
                    units[index] = units[index] with { Payload = units[index].Payload[..random.Next(units[index].Payload.Length + 1)] };
                    break;
                }

                case 10:
                {
                    // Padding, a temporal delimiter or a unit of a reserved type, anywhere
                    var payload = new byte[random.Next(8)];
                    for (var j = 0; j < payload.Length; j++)
                    {
                        payload[j] = random.NextByte();
                    }

                    var type = random.Pick(InsertedTypes);
                    units.Insert(random.Next(units.Count + 1), new Unit(type, Extension: null, type is TemporalDelimiter ? Array.Empty<byte>() : payload));
                    break;
                }

                case 11:
                {
                    // The stream ends at a unit
                    var kept = 1 + random.Next(units.Count);
                    units.RemoveRange(kept, units.Count - kept);
                    break;
                }

                default:
                {
                    // The temporal units of the stream: dropped, duplicated or swapped
                    var temporalUnits = GroupTemporalUnits(units);
                    if (temporalUnits.Count < 2)
                        break;

                    var a = random.Next(temporalUnits.Count);
                    var b = random.Next(temporalUnits.Count);
                    switch (random.Next(3))
                    {
                        case 0:
                            temporalUnits.RemoveAt(a);
                            break;
                        case 1:
                            temporalUnits.Insert(b, temporalUnits[a]);
                            break;
                        default:
                            (temporalUnits[a], temporalUnits[b]) = (temporalUnits[b], temporalUnits[a]);
                            break;
                    }

                    units = [.. temporalUnits.SelectMany(temporalUnit => temporalUnit)];
                    break;
                }
            }
        }

        if (units.Count is 0)
            return null;

        return FuzzAvifContainer.Wrap(Join(units), stream.Width, stream.Height, stream.BitDepth, stream.Subsampling);
    }

    /// <summary>Splits a stream into its units, or returns <see langword="null"/> when a unit has no size field or is cut.</summary>
    private static List<Unit>? Split(ReadOnlySpan<byte> data)
    {
        var units = new List<Unit>();
        var offset = 0;
        while (offset < data.Length)
        {
            var header = data[offset++];
            byte? extension = null;
            if ((header & 4) is not 0)
            {
                if (offset >= data.Length)
                    return null;

                extension = data[offset++];
            }

            if ((header & 2) is 0)
                return null;

            ulong size = 0;
            for (var i = 0; ; i++)
            {
                if (i is 8 || offset >= data.Length)
                    return null;

                var value = data[offset++];
                size |= (ulong)(value & 0x7F) << (7 * i);
                if ((value & 0x80) is 0)
                    break;
            }

            if (size > (ulong)(data.Length - offset))
                return null;

            units.Add(new Unit((header >> 3) & 15, extension, data.Slice(offset, (int)size).ToArray()));
            offset += (int)size;
        }

        return units;
    }

    private static byte[] Join(List<Unit> units)
    {
        var result = new List<byte>();
        foreach (var unit in units)
        {
            // The forbidden and reserved bits stay clear: the decoder rejects the first and ignores the second
            result.Add((byte)((unit.Type << 3) | (unit.Extension is null ? 0 : 4) | 2));
            if (unit.Extension is { } extension)
            {
                result.Add(extension);
            }

            var size = (uint)unit.Payload.Length;
            do
            {
                var value = (byte)(size & 0x7F);
                size >>= 7;
                result.Add(size is 0 ? value : (byte)(value | 0x80));
            }
            while (size is not 0);

            result.AddRange(unit.Payload);
        }

        return [.. result];
    }

    // A temporal unit starts at each temporal delimiter
    private static List<List<Unit>> GroupTemporalUnits(List<Unit> units)
    {
        var result = new List<List<Unit>>();
        foreach (var unit in units)
        {
            if (unit.Type is TemporalDelimiter || result.Count is 0)
            {
                result.Add([]);
            }

            result[^1].Add(unit);
        }

        return result;
    }

    private sealed record Unit(int Type, byte? Extension, byte[] Payload);
}
