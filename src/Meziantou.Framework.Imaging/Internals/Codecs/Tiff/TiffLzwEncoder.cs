using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The LZW encoder of TIFF strips (TIFF 6.0, section 13), written from the specification: the counterpart of
/// <see cref="TiffLzwDecoder"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// A strip starts with a clear code and ends with the end of information code. Codes are packed most significant bit
/// first and grow from 9 to 12 bits with the early change of the specification: 10 bits once the entry 511 is added, 11
/// bits after 1023 and 12 bits after 2047.
/// </description></item>
/// <item><description>
/// The table is cleared as soon as the entry 4093 is added, as the specification requires, so a decoder never has to
/// address a full table.
/// </description></item>
/// <item><description>
/// The strings of the table are found in an open-addressing hash table keyed by (prefix code, byte), rented once from the
/// writer scope as <see cref="AllocationKind.Temporary"/> and reused by every strip.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class TiffLzwEncoder : IDisposable
{
    // Twice the number of table codes: the table is at most half full, so a probe sequence stays short
    private const int SlotCount = 2 * TiffLzwDecoder.MaxCodes;
    private const int KeysOffset = 0;
    private const int CodesOffset = KeysOffset + (SlotCount * sizeof(int));
    private const int StateLength = CodesOffset + (SlotCount * sizeof(ushort));

    // The table is cleared when the next code would be this one (TIFF 6.0: "as soon as we use entry 4094")
    private const int ClearAtCode = TiffLzwDecoder.MaxCodes - 2;

    private PooledBuffer? _state;

    /// <summary>Allocates the string table.</summary>
    /// <param name="scope">The scope charged for it.</param>
    /// <exception cref="ImageResourceLimitException">The table exceeds the live-allocation limit.</exception>
    public TiffLzwEncoder(AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _state = scope.Rent(StateLength, AllocationKind.Temporary, clear: false);
    }

    /// <summary>Compresses one strip.</summary>
    /// <param name="source">The bytes of the strip.</param>
    /// <param name="output">The output buffer the compressed strip is appended to.</param>
    public void Encode(ReadOnlySpan<byte> source, ImageOutputBuffer output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var state = (_state ?? throw new ObjectDisposedException(nameof(TiffLzwEncoder))).Span;

        // A key is ((prefix code << 8) | byte) + 1, so that 0 is an empty slot
        var keys = unsafe(MemoryMarshal.Cast<byte, int>(state.Slice(KeysOffset, SlotCount * sizeof(int))));
        var codes = unsafe(MemoryMarshal.Cast<byte, ushort>(state.Slice(CodesOffset, SlotCount * sizeof(ushort))));
        keys.Clear();

        var writer = new TiffBitWriter(output);
        var codeSize = TiffLzwDecoder.MinCodeSize;
        var nextCode = TiffLzwDecoder.FirstTableCode;
        writer.Write(TiffLzwDecoder.ClearCode, codeSize);

        // The code of the longest string of the table that matches the bytes read so far, or -1 before the first byte
        var current = -1;
        foreach (var value in source)
        {
            if (current < 0)
            {
                current = value;
                continue;
            }

            var key = ((current << 8) | value) + 1;
            var slot = (int)(((uint)key * 2654435761u) >> 19);
            var found = false;
            while (keys[slot] != 0)
            {
                if (keys[slot] == key)
                {
                    current = codes[slot];
                    found = true;
                    break;
                }

                slot = (slot + 1) & (SlotCount - 1);
            }

            if (found)
                continue;

            writer.Write((uint)current, codeSize);
            keys[slot] = key;
            codes[slot] = (ushort)nextCode;
            nextCode++;
            if (nextCode == ClearAtCode)
            {
                writer.Write(TiffLzwDecoder.ClearCode, codeSize);
                keys.Clear();
                codeSize = TiffLzwDecoder.MinCodeSize;
                nextCode = TiffLzwDecoder.FirstTableCode;
            }
            else if (nextCode > (1 << codeSize) - 1)
            {
                codeSize++;
            }

            current = value;
        }

        if (current >= 0)
        {
            // A decoder adds an entry after every code but the first one: the last code counts toward the size of the
            // end of information code like any other
            writer.Write((uint)current, codeSize);
            nextCode++;
            if (nextCode == ClearAtCode)
            {
                writer.Write(TiffLzwDecoder.ClearCode, codeSize);
                codeSize = TiffLzwDecoder.MinCodeSize;
            }
            else if (nextCode > (1 << codeSize) - 1)
            {
                codeSize++;
            }
        }

        writer.Write(TiffLzwDecoder.EndOfInformationCode, codeSize);
        writer.Flush();
    }

    public void Dispose()
    {
        _state?.Dispose();
        _state = null;
    }
}
