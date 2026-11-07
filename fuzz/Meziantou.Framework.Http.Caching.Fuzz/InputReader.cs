using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Http.Caching.Fuzz;

/// <summary>Reads a structured input made of bytes and lines. A missing byte reads as 0 and a missing line as an empty text, so every input is valid.</summary>
internal sealed class InputReader
{
    private readonly byte[] _data;
    private int _position;

    public InputReader(ReadOnlySpan<byte> data)
    {
        _data = data.ToArray();
    }

    public bool IsEmpty => _position >= _data.Length;

    public byte ReadByte() => _position < _data.Length ? _data[_position++] : (byte)0;

    /// <summary>Reads a little-endian integer of <paramref name="size"/> bytes (at most 8), sign-extended.</summary>
    public long ReadInt64(int size)
    {
        ulong value = 0;
        for (var i = 0; i < size; i++)
        {
            value |= (ulong)ReadByte() << (8 * i);
        }

        var shift = 64 - (8 * size);
        return shift is 64 ? 0 : (long)(value << shift) >> shift;
    }

    /// <summary>Reads the text up to the next line feed, which is consumed.</summary>
    public string ReadLine()
    {
        var remaining = _data.AsSpan(_position);
        var index = remaining.IndexOf((byte)'\n');
        var line = index < 0 ? remaining : remaining[..index];
        _position += index < 0 ? remaining.Length : index + 1;
        return FuzzText.Decode(line);
    }

    public byte[] ReadRemainingBytes()
    {
        var result = _data.AsSpan(_position).ToArray();
        _position = _data.Length;
        return result;
    }

    /// <summary>Writes an input for this reader: a <see cref="byte"/> or an <see cref="int"/> is a byte, a <see cref="long"/> is 8 bytes (little-endian), a string is a line (its line feed included).</summary>
    public static byte[] Create(params object[] parts)
    {
        var result = new List<byte>();
        foreach (var part in parts)
        {
            switch (part)
            {
                case byte value:
                    result.Add(value);
                    break;

                case int value:
                    result.Add(checked((byte)value));
                    break;

                case long value:
                    for (var i = 0; i < 8; i++)
                    {
                        result.Add((byte)(value >> (8 * i)));
                    }

                    break;

                case string value:
                    result.AddRange(FuzzText.Encode(value + "\n"));
                    break;

                default:
                    throw new ArgumentException($"Unsupported part type '{part.GetType()}'", nameof(parts));
            }
        }

        return [.. result];
    }
}
