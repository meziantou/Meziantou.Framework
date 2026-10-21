using System.Buffers.Binary;

namespace Meziantou.Framework.JavaScriptEngine.Data.Generator;

/// <summary>Reads the 64-bit data of a TZif file (RFC 9636), as written by zic.</summary>
internal static class TzifReader
{
    public static (List<(long Time, CompiledType Type)> Transitions, CompiledType InitialType) Read(byte[] data)
    {
        if (data.Length < 44 || data[0] != 'T' || data[1] != 'Z' || data[2] != 'i' || data[3] != 'f')
            throw new InvalidDataException("Not a TZif file");

        var version = data[4];
        var position = 0;
        var (timeCount, typeCount, charCount, leapCount, isStdCount, isUtCount) = ReadHeader(data, position);
        position += 44;
        if (version >= '2')
        {
            // Skip the 32-bit data
            position += (timeCount * 4) + timeCount + (typeCount * 6) + charCount + (leapCount * 8) + isStdCount + isUtCount;
            (timeCount, typeCount, charCount, _, _, _) = ReadHeader(data, position);
            position += 44;
            return ReadData(data, position, timeSize: 8, timeCount, typeCount, charCount);
        }

        return ReadData(data, position, timeSize: 4, timeCount, typeCount, charCount);
    }

    private static (int TimeCount, int TypeCount, int CharCount, int LeapCount, int IsStdCount, int IsUtCount) ReadHeader(byte[] data, int position)
    {
        int ReadCount(int index) => BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(position + 20 + (index * 4)));
        return (ReadCount(3), ReadCount(4), ReadCount(5), ReadCount(2), ReadCount(1), ReadCount(0));
    }

    private static (List<(long Time, CompiledType Type)>, CompiledType) ReadData(byte[] data, int position, int timeSize, int timeCount, int typeCount, int charCount)
    {
        var times = new long[timeCount];
        for (var i = 0; i < timeCount; i++)
        {
            times[i] = timeSize == 8 ? BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(position)) : BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(position));
            position += timeSize;
        }

        var typeIndexes = data.AsSpan(position, timeCount).ToArray();
        position += timeCount;
        var rawTypes = new (int Offset, bool IsDst, int AbbreviationIndex)[typeCount];
        for (var i = 0; i < typeCount; i++)
        {
            rawTypes[i] = (BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(position)), data[position + 4] != 0, data[position + 5]);
            position += 6;
        }

        var chars = data.AsSpan(position, charCount).ToArray();
        var types = rawTypes.Select(type =>
        {
            var end = chars.AsSpan(type.AbbreviationIndex).IndexOf((byte)0);
            return new CompiledType(type.Offset, type.IsDst, Encoding.ASCII.GetString(chars.AsSpan(type.AbbreviationIndex, end)));
        }).ToArray();

        var transitions = new List<(long, CompiledType)>();
        for (var i = 0; i < timeCount; i++)
        {
            transitions.Add((times[i], types[typeIndexes[i]]));
        }

        return (transitions, types[0]);
    }
}
