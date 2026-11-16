namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>
/// Decodes the Unicode file names of RAR 2.9-4.x: the name is stored as an OEM name, a zero byte, then a compact
/// encoding of the UTF-16 name that refers to the OEM bytes. The algorithm follows libarchive (BSD-2-Clause).
/// </summary>
internal static class RarUnicodeName
{
    private const int MaxLength = 0x10000;

    /// <summary>Decodes the name, or returns <see langword="null"/> when the encoding is invalid.</summary>
    public static string? Decode(ReadOnlySpan<byte> oemName, ReadOnlySpan<byte> encoded)
    {
        if (encoded.IsEmpty)
            return null;

        var result = new char[Math.Min(MaxLength, Math.Max(oemName.Length, encoded.Length) * 2 + 1)];
        var length = 0;
        var position = 0;
        var highByte = encoded[position++];
        var flags = 0;
        var flagBits = 0;
        while (position < encoded.Length && length < result.Length)
        {
            if (flagBits is 0)
            {
                flags = encoded[position++];
                flagBits = 8;
            }

            switch (flags >> 6)
            {
                case 0:
                    if (position >= encoded.Length)
                        return null;

                    result[length++] = (char)encoded[position++];
                    break;

                case 1:
                    if (position >= encoded.Length)
                        return null;

                    result[length++] = (char)(encoded[position++] | (highByte << 8));
                    break;

                case 2:
                    if (position + 1 >= encoded.Length)
                        return null;

                    result[length++] = (char)(encoded[position] | (encoded[position + 1] << 8));
                    position += 2;
                    break;

                default:
                    {
                        if (position >= encoded.Length)
                            return null;

                        int count = encoded[position++];
                        if ((count & 0x80) != 0)
                        {
                            if (position >= encoded.Length)
                                return null;

                            var correction = encoded[position++];
                            for (count = (count & 0x7F) + 2; count > 0 && length < result.Length; count--, length++)
                            {
                                if (length >= oemName.Length)
                                    return null;

                                result[length] = (char)(((oemName[length] + correction) & 0xFF) | (highByte << 8));
                            }
                        }
                        else
                        {
                            for (count += 2; count > 0 && length < result.Length; count--, length++)
                            {
                                if (length >= oemName.Length)
                                    return null;

                                result[length] = (char)oemName[length];
                            }
                        }

                        break;
                    }
            }

            flags = (flags << 2) & 0xFF;
            flagBits -= 2;
        }

        var nameLength = Array.IndexOf(result, '\0', 0, length);
        return new string(result, 0, nameLength < 0 ? length : nameLength);
    }
}
