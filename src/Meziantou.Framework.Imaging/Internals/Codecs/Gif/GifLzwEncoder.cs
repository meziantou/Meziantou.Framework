using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The variable-length-code LZW encoder of GIF image data (GIF89a specification, Appendix F), written from the specification;
/// the inverse of <see cref="GifLzwDecoder"/>. It is incremental: color indices are pushed in any pieces (typically one row)
/// and the codes are appended to the output as data sub-blocks of at most 255 bytes, so neither the indices of the image nor
/// the compressed datastream is ever buffered as a whole.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Codes are packed least-significant bit first. With a minimum code size <c>m</c> (2 to 8 here: GIF color tables have at
/// most 256 entries, and 2 is the smallest value every decoder accepts), the clear code is <c>2^m</c>, the end code
/// <c>2^m + 1</c> and the first table code <c>2^m + 2</c>; codes start with <c>m + 1</c> bits.
/// </description></item>
/// <item><description>
/// The datastream starts with a clear code. Each emitted code adds the table entry "emitted string + next index"; the code size
/// grows by one bit, up to 12, as soon as the next code to assign reaches <c>2^size</c> when a code is emitted: the decoder
/// adds the matching entry one code later and grows when its own next code reaches <c>2^size</c>, so both sides change size
/// between the same two codes (<see cref="GifLzwDecoder"/> documents the decoding rule).
/// </description></item>
/// <item><description>
/// When the table is full (4096 codes), a clear code is emitted and the table restarts (no deferred clear). The datastream ends
/// with the code of the pending string, the end code, the padding bits of the last byte, and the block terminator.
/// </description></item>
/// <item><description>
/// The string table is a hash table of (prefix code, index) keys with linear probing (8192 slots, at most half full), rented
/// once from the writer scope as <see cref="AllocationKind.Temporary"/> and reused by every image.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class GifLzwEncoder : IDisposable
{
    /// <summary>The number of codes of a full table (12-bit codes).</summary>
    public const int MaxCodes = 4096;

    /// <summary>The largest data sub-block.</summary>
    public const int MaxSubBlockLength = 255;

    private const int MaxCodeSize = 12;
    private const int HashBits = 13;
    private const int HashSlots = 1 << HashBits;
    private const int KeysOffset = 0;
    private const int CodesOffset = HashSlots * sizeof(int);
    private const int SubBlockOffset = CodesOffset + (HashSlots * sizeof(ushort));
    private const int StateLength = SubBlockOffset + MaxSubBlockLength;

    private PooledBuffer? _state;
    private int _clearCode;
    private int _minimumCodeSize;
    private int _codeSize;
    private int _nextCode;
    private int _prefix;
    private ulong _bits;
    private int _bitCount;
    private int _subBlockLength;
    private bool _started;

    /// <summary>Allocates the string table.</summary>
    /// <param name="scope">The scope charged for the table.</param>
    /// <exception cref="ImageResourceLimitException">The table exceeds the live-allocation limit.</exception>
    public GifLzwEncoder(AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _state = scope.Rent(StateLength, AllocationKind.Temporary, clear: false);
    }

    /// <summary>Gets the minimum code size used for a color table of <paramref name="colorTableEntries"/> entries (a power of two, 2 to 256).</summary>
    /// <param name="colorTableEntries">The number of color table entries.</param>
    /// <returns>The LZW minimum code size: <c>max(2, log2(entries))</c>.</returns>
    public static int GetMinimumCodeSize(int colorTableEntries)
    {
        var bits = 1;
        while ((1 << bits) < colorTableEntries)
        {
            bits++;
        }

        return Math.Max(2, bits);
    }

    /// <summary>Starts a new image datastream (the caller writes the minimum code size byte before it).</summary>
    /// <param name="minimumCodeSize">The LZW minimum code size (2 to 8).</param>
    public void Reset(int minimumCodeSize)
    {
        if (minimumCodeSize is < 2 or > 8)
            throw new ArgumentOutOfRangeException(nameof(minimumCodeSize), minimumCodeSize, "The LZW minimum code size must be 2 to 8.");

        _minimumCodeSize = minimumCodeSize;
        _clearCode = 1 << minimumCodeSize;
        _bits = 0;
        _bitCount = 0;
        _subBlockLength = 0;
        _prefix = -1;
        _started = false;
    }

    /// <summary>Appends color indices to the datastream.</summary>
    /// <param name="indices">The indices (each below <c>2^minimumCodeSize</c>).</param>
    /// <param name="output">The output receiving the completed data sub-blocks.</param>
    public void Write(ReadOnlySpan<byte> indices, ImageOutputBuffer output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var state = GetState();
        var keys = unsafe(MemoryMarshal.Cast<byte, int>(state.Slice(KeysOffset, HashSlots * sizeof(int))));
        var codes = unsafe(MemoryMarshal.Cast<byte, ushort>(state.Slice(CodesOffset, HashSlots * sizeof(ushort))));
        if (!_started)
        {
            _started = true;
            ClearTable(keys);
            EmitCode(_clearCode, output);
        }

        var prefix = _prefix;
        foreach (var index in indices)
        {
            if (index >= _clearCode)
                throw new ArgumentOutOfRangeException(nameof(indices), index, "The color index is outside the code space.");

            if (prefix < 0)
            {
                prefix = index;
                continue;
            }

            // Look up the string "prefix + index"
            var key = (prefix << 8) | index;
            var slot = Hash(key);
            var found = false;
            while (keys[slot] >= 0)
            {
                if (keys[slot] == key)
                {
                    found = true;
                    break;
                }

                slot = (slot + 1) & (HashSlots - 1);
            }

            if (found)
            {
                prefix = codes[slot];
                continue;
            }

            // Not in the table: emit the prefix, then add the new string (or restart a full table)
            EmitDataCode(prefix, output);
            if (_nextCode < MaxCodes)
            {
                keys[slot] = key;
                codes[slot] = (ushort)_nextCode;
                _nextCode++;
            }
            else
            {
                EmitCode(_clearCode, output);
                ClearTable(keys);
            }

            prefix = index;
        }

        _prefix = prefix;
    }

    /// <summary>Ends the datastream: the pending string, the end code, the padding bits, the last sub-block and the block terminator.</summary>
    /// <param name="output">The output.</param>
    public void Finish(ImageOutputBuffer output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!_started)
        {
            // An image always has at least one pixel; an empty datastream still starts with a clear code
            _started = true;
            ClearTable(unsafe(MemoryMarshal.Cast<byte, int>(GetState().Slice(KeysOffset, HashSlots * sizeof(int)))));
            EmitCode(_clearCode, output);
        }

        if (_prefix >= 0)
        {
            EmitDataCode(_prefix, output);
            _prefix = -1;
        }

        EmitCode(_clearCode + 1, output);
        if (_bitCount > 0)
        {
            AppendByte((byte)_bits, output);
            _bits = 0;
            _bitCount = 0;
        }

        FlushSubBlock(output);
        output.Write([0]);
    }

    public void Dispose()
    {
        _state?.Dispose();
        _state = null;
    }

    private static int Hash(int key) => (int)(((uint)key * 0x9E3779B1u) >> (32 - HashBits));

    private void ClearTable(Span<int> keys)
    {
        keys.Fill(-1);
        _codeSize = _minimumCodeSize + 1;
        _nextCode = _clearCode + 2;
    }

    /// <summary>Emits a data code, then grows the code size when the next code to assign reaches the code space.</summary>
    private void EmitDataCode(int code, ImageOutputBuffer output)
    {
        EmitCode(code, output);
        if (_nextCode >= 1 << _codeSize && _codeSize < MaxCodeSize)
        {
            _codeSize++;
        }
    }

    private void EmitCode(int code, ImageOutputBuffer output)
    {
        _bits |= (ulong)(uint)code << _bitCount;
        _bitCount += _codeSize;
        while (_bitCount >= 8)
        {
            AppendByte((byte)_bits, output);
            _bits >>= 8;
            _bitCount -= 8;
        }
    }

    private void AppendByte(byte value, ImageOutputBuffer output)
    {
        var subBlock = GetState().Slice(SubBlockOffset, MaxSubBlockLength);
        subBlock[_subBlockLength++] = value;
        if (_subBlockLength == MaxSubBlockLength)
        {
            FlushSubBlock(output);
        }
    }

    private void FlushSubBlock(ImageOutputBuffer output)
    {
        if (_subBlockLength == 0)
            return;

        var span = output.GetSpan(_subBlockLength + 1);
        span[0] = (byte)_subBlockLength;
        GetState().Slice(SubBlockOffset, _subBlockLength).CopyTo(span[1..]);
        output.Advance(_subBlockLength + 1);
        _subBlockLength = 0;
    }

    private Span<byte> GetState() => (_state ?? throw new ObjectDisposedException(nameof(GifLzwEncoder))).RawBuffer.AsSpan(0, StateLength);
}
