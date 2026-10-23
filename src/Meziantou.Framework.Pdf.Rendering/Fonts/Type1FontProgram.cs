using System.Globalization;
using System.Text;
using Meziantou.Framework.Fonts;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Pdf.Rendering.Fonts;

/// <summary>
/// A Type 1 font program (Adobe Type 1 Font Format) embedded as <c>/FontFile</c>: the clear-text part gives the font
/// matrix and the built-in encoding, the part encrypted with <c>eexec</c> the subroutines and the charstrings, which
/// <see cref="Type1CharstringInterpreter"/> runs.
/// </summary>
internal sealed class Type1FontProgram
{
    private const int MaxCharstrings = 65536;

    private readonly Dictionary<string, byte[]> _charstrings = new(StringComparer.Ordinal);
    private readonly List<byte[]> _subroutines = [];

    private Type1FontProgram()
    {
    }

    public PdfMatrix FontMatrix { get; private set; } = new(0.001, 0, 0, 0.001, 0, 0);

    /// <summary>Gets the built-in encoding: the glyph name of each code, or <see langword="null"/> for the standard encoding.</summary>
    public string?[]? BuiltinEncoding { get; private set; }

    public bool HasGlyph(string name) => _charstrings.ContainsKey(name);

    public IReadOnlyList<byte[]> Subroutines => _subroutines;

    public byte[]? GetCharstring(string name) => _charstrings.GetValueOrDefault(name);

    public static Type1FontProgram Read(byte[] data, long length2)
    {
        data = StripPfbHeaders(data);
        var program = new Type1FontProgram();

        // The clear text ends with "eexec", whatever /Length1 says
        var eexec = data.AsSpan().IndexOf("eexec"u8);
        if (eexec < 0)
            throw new InvalidFontException("The Type 1 font program has no encrypted part.");

        var clearText = Encoding.Latin1.GetString(data, 0, eexec);
        program.ReadClearText(clearText);

        var start = eexec + 5;
        while (start < data.Length && data[start] is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t')
        {
            start++;
        }

        var end = length2 > 0 && start + length2 <= data.Length ? start + (int)length2 : data.Length;
        var encrypted = data.AsSpan(start, end - start);
        if (IsHex(encrypted))
        {
            encrypted = FromHex(encrypted);
        }

        var decrypted = Decrypt(encrypted, 55665, skip: 4);
        program.ReadPrivate(decrypted);
        return program;
    }

    /// <summary>Gets the outline of a glyph, in the units of the charstrings.</summary>
    public VectorPath? GetPath(string name, RenderCache cache)
    {
        if (GetCharstring(name) is not { } charstring)
            return null;

        var sink = new GlyphPathSink();
        var interpreter = new Type1CharstringInterpreter(this, sink, cache);
        interpreter.Run(charstring);
        return sink.ToPath();
    }

    /// <summary>Decrypts the <c>eexec</c> part or a charstring (Type 1 Font Format, 7): r is 55665 or 4330, the first bytes are random.</summary>
    internal static byte[] Decrypt(ReadOnlySpan<byte> data, int key, int skip)
    {
        var r = key;
        var result = new byte[Math.Max(0, data.Length - skip)];
        for (var i = 0; i < data.Length; i++)
        {
            var cipher = data[i];
            var plain = (byte)(cipher ^ (r >> 8));
            r = (((cipher + r) * 52845) + 22719) & 0xFFFF;
            if (i >= skip)
            {
                result[i - skip] = plain;
            }
        }

        return result;
    }

    private static byte[] StripPfbHeaders(byte[] data)
    {
        // A PFB file: segments of a 6-byte header (0x80, type, length) that are not part of the font program
        if (data.Length < 6 || data[0] != 0x80)
            return data;

        var output = new List<byte>(data.Length);
        var position = 0;
        while (position + 6 <= data.Length && data[position] == 0x80 && data[position + 1] is 1 or 2)
        {
            var length = data[position + 2] | (data[position + 3] << 8) | (data[position + 4] << 16) | (data[position + 5] << 24);
            position += 6;
            if (length < 0 || position + length > data.Length)
            {
                length = data.Length - position;
            }

            output.AddRange(data.AsSpan(position, length));
            position += length;
        }

        return [.. output];
    }

    private static bool IsHex(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
            return false;

        for (var i = 0; i < 4; i++)
        {
            if (!char.IsAsciiHexDigit((char)data[i]))
                return false;
        }

        return true;
    }

    private static byte[] FromHex(ReadOnlySpan<byte> data)
    {
        var result = new List<byte>(data.Length / 2);
        var high = -1;
        foreach (var b in data)
        {
            var value = b switch
            {
                >= (byte)'0' and <= (byte)'9' => b - '0',
                >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
                >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
                _ => -1,
            };
            if (value < 0)
                continue;

            if (high < 0)
            {
                high = value;
            }
            else
            {
                result.Add((byte)((high << 4) | value));
                high = -1;
            }
        }

        return [.. result];
    }

    private void ReadClearText(string text)
    {
        var matrixIndex = text.IndexOf("/FontMatrix", StringComparison.Ordinal);
        if (matrixIndex >= 0)
        {
            var open = text.IndexOfAny(['[', '{'], matrixIndex);
            var close = open >= 0 ? text.IndexOfAny([']', '}'], open) : -1;
            if (close > open)
            {
                var values = text[(open + 1)..close].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(value => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : double.NaN).ToArray();
                if (values.Length == 6 && values.All(double.IsFinite))
                {
                    FontMatrix = new PdfMatrix(values[0], values[1], values[2], values[3], values[4], values[5]);
                }
            }
        }

        var encodingIndex = text.IndexOf("/Encoding", StringComparison.Ordinal);
        if (encodingIndex >= 0 && !text.AsSpan(encodingIndex).TrimStart("/Encoding").TrimStart().StartsWith("StandardEncoding", StringComparison.Ordinal))
        {
            // dup <code> /<name> put, up to the end of the array
            var encoding = new string?[256];
            var endOfArray = text.IndexOf("readonly def", encodingIndex, StringComparison.Ordinal);
            var section = text[encodingIndex..(endOfArray > encodingIndex ? endOfArray : text.Length)];
            var position = 0;
            while (true)
            {
                var dup = section.IndexOf("dup ", position, StringComparison.Ordinal);
                if (dup < 0)
                    break;

                position = dup + 4;
                var parts = section[position..Math.Min(section.Length, position + 200)].Split((char[]?)null, 4, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && parts[2].StartsWith("put", StringComparison.Ordinal) && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) && code is >= 0 and < 256 && parts[1][0] == '/')
                {
                    encoding[code] = parts[1][1..];
                }
            }

            BuiltinEncoding = encoding;
        }
    }

    /// <summary>Reads the private dictionary: /lenIV, the subroutines and the charstrings, each a binary string after RD or -|.</summary>
    private void ReadPrivate(byte[] data)
    {
        var lenIV = 4;
        var lenIVIndex = data.AsSpan().IndexOf("/lenIV"u8);
        if (lenIVIndex >= 0)
        {
            var position = lenIVIndex + 6;
            if (TryReadInteger(data, ref position, out var value))
            {
                lenIV = value;
            }
        }

        var subrs = data.AsSpan().IndexOf("/Subrs"u8);
        var charStrings = data.AsSpan().IndexOf("/CharStrings"u8);
        if (subrs >= 0)
        {
            var position = subrs + 6;
            var end = charStrings > subrs ? charStrings : data.Length;
            while (position < end)
            {
                var dup = data.AsSpan(position, end - position).IndexOf("dup"u8);
                if (dup < 0)
                    break;

                position += dup + 3;
                if (!TryReadInteger(data, ref position, out var index) || !TryReadInteger(data, ref position, out var length) || !TryReadBinary(data, ref position, length, out var binary))
                    continue;

                while (_subroutines.Count <= index && _subroutines.Count < MaxCharstrings)
                {
                    _subroutines.Add([]);
                }

                if (index >= 0 && index < _subroutines.Count)
                {
                    _subroutines[index] = DecryptCharstring(binary, lenIV);
                }
            }
        }

        if (charStrings >= 0)
        {
            var position = charStrings + 12;
            var begin = data.AsSpan(position).IndexOf("begin"u8);
            if (begin < 0)
                return;

            position += begin + 5;
            while (position < data.Length && _charstrings.Count < MaxCharstrings)
            {
                var slash = data.AsSpan(position).IndexOf((byte)'/');
                if (slash < 0)
                    break;

                // The end of the dictionary comes before the next name
                var endIndex = data.AsSpan(position, slash).IndexOf("end"u8);
                if (endIndex >= 0)
                    break;

                position += slash + 1;
                var nameStart = position;
                while (position < data.Length && !IsDelimiter(data[position]))
                {
                    position++;
                }

                var name = Encoding.Latin1.GetString(data, nameStart, position - nameStart);
                if (!TryReadInteger(data, ref position, out var length) || !TryReadBinary(data, ref position, length, out var binary))
                    continue;

                _charstrings.TryAdd(name, DecryptCharstring(binary, lenIV));
            }
        }
    }

    private static byte[] DecryptCharstring(ReadOnlySpan<byte> binary, int lenIV) => lenIV < 0 ? binary.ToArray() : Decrypt(binary, 4330, lenIV);

    private static bool IsDelimiter(byte value) => value is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t' or (byte)'/' or (byte)'[' or (byte)'{' or (byte)'(';

    private static bool TryReadInteger(byte[] data, ref int position, out int value)
    {
        value = 0;
        while (position < data.Length && data[position] is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t')
        {
            position++;
        }

        var start = position;
        if (position < data.Length && data[position] == '-')
        {
            position++;
        }

        while (position < data.Length && data[position] is >= (byte)'0' and <= (byte)'9')
        {
            position++;
        }

        return position > start && int.TryParse(data.AsSpan(start, position - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Reads the token that introduces a binary string (RD, -| or another name), one space, then the bytes.</summary>
    private static bool TryReadBinary(byte[] data, ref int position, int length, out ReadOnlySpan<byte> binary)
    {
        binary = default;
        while (position < data.Length && data[position] is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t')
        {
            position++;
        }

        while (position < data.Length && data[position] is not ((byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t'))
        {
            position++;
        }

        position++;
        if (length < 0 || position + length > data.Length)
            return false;

        binary = data.AsSpan(position, length);
        position += length;
        return true;
    }
}
