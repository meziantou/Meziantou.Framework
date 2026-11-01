using System.Runtime.InteropServices;

namespace Meziantou.Framework.Barcodes.Internal;

/// <summary>
/// A run of the text encoded in one mode, with its own mode indicator and character count indicator.
/// </summary>
/// <param name="Mode">The encoding mode of the run.</param>
/// <param name="Start">The index of the first UTF-16 code unit of the run.</param>
/// <param name="Length">The number of UTF-16 code units of the run.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct QRSegment(EncodingMode Mode, int Start, int Length)
{
    /// <summary>Gets the value of the character count indicator: characters, or bytes in byte mode.</summary>
    public int GetCharacterCount(string data)
    {
        return Mode switch
        {
            EncodingMode.Byte => Encoding.UTF8.GetByteCount(data.AsSpan(Start, Length)),
            _ => Length,
        };
    }

    /// <summary>Gets the number of bits of the encoded characters, without the mode and count indicators.</summary>
    public int GetDataBitCount(string data)
    {
        return Mode switch
        {
            EncodingMode.Numeric => (Length / 3 * 10) + ((Length % 3) switch { 2 => 7, 1 => 4, _ => 0 }),
            EncodingMode.Alphanumeric => (Length / 2 * 11) + (Length % 2 * 6),
            EncodingMode.Byte => Encoding.UTF8.GetByteCount(data.AsSpan(Start, Length)) * 8,
            EncodingMode.Kanji => Length * 13,
            _ => throw new InvalidOperationException($"Unexpected mode {Mode}."),
        };
    }
}
