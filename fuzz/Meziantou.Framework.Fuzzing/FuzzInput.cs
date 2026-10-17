namespace Meziantou.Framework.Fuzzing;

/// <summary>Reads a structured input: option bytes, then texts. A missing byte reads as 0, so every input is valid.</summary>
public static class FuzzInput
{
    /// <summary>Reads the first byte (0 when the input is empty) and removes it from <paramref name="data"/>.</summary>
    public static byte ReadByte(ref ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return 0;

        var value = data[0];
        data = data[1..];
        return value;
    }

    /// <summary>Reads the bytes up to the first <paramref name="separator"/> (or to the end) and removes them, with the separator, from <paramref name="data"/>.</summary>
    public static ReadOnlySpan<byte> ReadUntil(ref ReadOnlySpan<byte> data, byte separator)
    {
        var index = data.IndexOf(separator);
        if (index < 0)
        {
            var all = data;
            data = default;
            return all;
        }

        var part = data[..index];
        data = data[(index + 1)..];
        return part;
    }

    /// <summary>Decodes the remaining bytes as a text (<see cref="FuzzText.Decode"/>).</summary>
    public static string ReadText(ReadOnlySpan<byte> data) => FuzzText.Decode(data);

    /// <summary>Picks an item of a list from a byte.</summary>
    public static T Pick<T>(IReadOnlyList<T> items, byte value) => items[value % items.Count];
}
