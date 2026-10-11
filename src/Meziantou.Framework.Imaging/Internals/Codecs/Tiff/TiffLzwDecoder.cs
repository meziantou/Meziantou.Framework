using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The LZW decoder of TIFF strips and tiles (TIFF 6.0, section 13), written from the specification.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Codes are packed most significant bit first. Codes 0 to 255 are the bytes, 256 is the clear code, 257 the end of
/// information code and 258 the first table code. Codes start with 9 bits and grow to 10, 11 and 12 bits one code
/// <em>before</em> the table needs them (after the entries 510, 1022 and 2046: the "early change" of the specification).
/// </description></item>
/// <item><description>
/// The decompressed size of a block is known before it is read, so the decoder fills a buffer of exactly that size: the
/// bytes a block produces beyond it are not decoded, and a block that ends before it (the data runs out, or the end of
/// information code comes first) reports how many bytes it produced.
/// </description></item>
/// <item><description>
/// A full table is not an error: the decoder keeps reading 12-bit codes without adding entries until a clear code. A block
/// may start without a clear code (the table starts initialized).
/// </description></item>
/// <item><description>
/// Malformed codes are reported, never guessed: a code above the next table code, the next table code itself when no
/// previous code exists, and a table code as the first code after a clear.
/// </description></item>
/// <item><description>
/// Working memory: the code table (prefix, length, last and first byte of every code), rented once from the operation
/// scope as <see cref="AllocationKind.DecoderState"/> and reused by every block.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class TiffLzwDecoder : IDisposable
{
    /// <summary>The code that resets the table.</summary>
    public const int ClearCode = 256;

    /// <summary>The code that ends a block.</summary>
    public const int EndOfInformationCode = 257;

    /// <summary>The first code of the table.</summary>
    public const int FirstTableCode = 258;

    /// <summary>The number of codes of a full table (12-bit codes).</summary>
    public const int MaxCodes = 4096;

    /// <summary>The number of bits of the first codes of a block, and of the codes that follow a clear code.</summary>
    public const int MinCodeSize = 9;

    /// <summary>The largest number of bits of a code.</summary>
    public const int MaxCodeSize = 12;

    private const int PrefixOffset = 0;
    private const int LengthOffset = PrefixOffset + (MaxCodes * sizeof(ushort));
    private const int SuffixOffset = LengthOffset + (MaxCodes * sizeof(ushort));
    private const int FirstOffset = SuffixOffset + MaxCodes;
    private const int StateLength = FirstOffset + MaxCodes;

    private PooledBuffer? _state;

    /// <summary>Allocates the code table.</summary>
    /// <param name="scope">The scope charged for it.</param>
    /// <exception cref="ImageResourceLimitException">The table exceeds the live-allocation limit.</exception>
    public TiffLzwDecoder(AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _state = scope.Rent(StateLength, AllocationKind.DecoderState, clear: false);
    }

    /// <summary>
    /// Determines whether a block uses the bit order of the LZW of TIFF 5.0 drafts (least significant bit first, no early
    /// change), which this decoder does not read: such a block starts with the clear code written in that order.
    /// </summary>
    /// <param name="source">The compressed block.</param>
    /// <returns><see langword="true"/> for the old bit order.</returns>
    public static bool UsesOldBitOrder(ReadOnlySpan<byte> source) => source.Length >= 2 && source[0] == 0 && (source[1] & 1) != 0;

    /// <summary>Decodes one block.</summary>
    /// <param name="source">The compressed block.</param>
    /// <param name="destination">The buffer to fill.</param>
    /// <param name="written">The number of bytes produced: the length of <paramref name="destination"/>, or fewer when the block ends first.</param>
    /// <returns><see langword="false"/> when a code is malformed.</returns>
    public bool TryDecode(ReadOnlySpan<byte> source, Span<byte> destination, out int written)
    {
        var state = (_state ?? throw new ObjectDisposedException(nameof(TiffLzwDecoder))).Span;
        var prefix = unsafe(MemoryMarshal.Cast<byte, ushort>(state.Slice(PrefixOffset, MaxCodes * sizeof(ushort))));
        var length = unsafe(MemoryMarshal.Cast<byte, ushort>(state.Slice(LengthOffset, MaxCodes * sizeof(ushort))));
        var suffix = state.Slice(SuffixOffset, MaxCodes);
        var first = state.Slice(FirstOffset, MaxCodes);
        for (var i = 0; i < 256; i++)
        {
            length[i] = 1;
            suffix[i] = (byte)i;
            first[i] = (byte)i;
        }

        var codeSize = MinCodeSize;
        var nextCode = FirstTableCode;
        var previous = -1;
        ulong bits = 0;
        var bitCount = 0;
        var position = 0;
        written = 0;
        while (written < destination.Length)
        {
            while (bitCount < codeSize)
            {
                if (position == source.Length)
                    return true;

                bits = (bits << 8) | source[position++];
                bitCount += 8;
            }

            bitCount -= codeSize;
            var code = (int)((bits >> bitCount) & ((1u << codeSize) - 1));
            if (code == ClearCode)
            {
                codeSize = MinCodeSize;
                nextCode = FirstTableCode;
                previous = -1;
                continue;
            }

            if (code == EndOfInformationCode)
                return true;

            if (previous < 0)
            {
                // The first code after a clear is a byte: there is no previous string to extend
                if (code >= ClearCode)
                    return false;

                destination[written++] = (byte)code;
                previous = code;
                continue;
            }

            if (code > nextCode || (code == nextCode && nextCode == MaxCodes))
                return false;

            if (nextCode < MaxCodes)
            {
                // The new entry is the previous string followed by the first byte of the current one; when the code is the
                // entry being defined, the current string starts like the previous one
                prefix[nextCode] = (ushort)previous;
                first[nextCode] = first[previous];
                length[nextCode] = (ushort)(length[previous] + 1);
                suffix[nextCode] = code == nextCode ? first[previous] : first[code];
            }

            // The string is written backward from its last byte; the bytes that do not fit in the block are not written
            var stringLength = (int)length[code];
            var count = Math.Min(stringLength, destination.Length - written);
            var entry = code;
            for (var skip = stringLength - count; skip > 0; skip--)
            {
                entry = prefix[entry];
            }

            var target = destination.Slice(written, count);
            for (var i = count - 1; i > 0; i--)
            {
                target[i] = suffix[entry];
                entry = prefix[entry];
            }

            target[0] = suffix[entry];
            written += count;
            if (nextCode < MaxCodes)
            {
                nextCode++;

                // Early change: the size grows when the next entry is the last one the current size could address
                if (nextCode > (1 << codeSize) - 2 && codeSize < MaxCodeSize)
                {
                    codeSize++;
                }
            }

            previous = code;
        }

        return true;
    }

    public void Dispose()
    {
        _state?.Dispose();
        _state = null;
    }
}
