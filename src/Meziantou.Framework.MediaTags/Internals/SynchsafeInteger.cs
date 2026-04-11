namespace Meziantou.Framework.MediaTags.Internals;

/// <summary>
/// Encodes and decodes synchsafe integers used in ID3v2 tags.
/// In a synchsafe integer, the most significant bit of each byte is zero,
/// making the total 28 bits effective in 4 bytes.
/// </summary>
internal static class SynchsafeInteger
{
    /// <summary>
    /// Decodes a 4-byte synchsafe integer.
    /// </summary>
    public static int Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
            throw new ArgumentException("Need at least 4 bytes to decode a synchsafe integer.", nameof(data));

        return (data[0] << 21) | (data[1] << 14) | (data[2] << 7) | data[3];
    }

    /// <summary>
    /// Encodes a value as a 4-byte synchsafe integer.
    /// </summary>
    public static void Encode(int value, Span<byte> destination)
    {
        if (destination.Length < 4)
            throw new ArgumentException("Need at least 4 bytes to encode a synchsafe integer.", nameof(destination));

        if (value < 0 || value > 0x0FFFFFFF)
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be between 0 and 0x0FFFFFFF (268435455).");

        destination[0] = (byte)((value >> 21) & 0x7F);
        destination[1] = (byte)((value >> 14) & 0x7F);
        destination[2] = (byte)((value >> 7) & 0x7F);
        destination[3] = (byte)(value & 0x7F);
    }
}
