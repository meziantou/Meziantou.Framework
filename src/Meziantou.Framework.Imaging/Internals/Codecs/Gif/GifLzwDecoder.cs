using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The variable-length-code LZW decoder of GIF image data (GIF89a specification, Appendix F), written from the specification.
/// It is incremental: input bytes (the concatenated data sub-blocks) are pushed in any pieces and color indices are pulled into
/// caller buffers of any size, so neither the compressed data nor the decoded image is ever buffered as a whole.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Codes are packed least-significant bit first. With a minimum code size <c>m</c> (1 to 11), the clear code is <c>2^m</c>, the
/// end code <c>2^m + 1</c>, the first table code <c>2^m + 2</c>, and codes start with <c>m + 1</c> bits. The code size grows by
/// one bit when the next table code reaches <c>2^size</c>, up to 12 bits.
/// </description></item>
/// <item><description>
/// A full table (4096 codes) is not an error: the decoder keeps reading 12-bit codes without adding entries until a clear code
/// (the "deferred clear" used by some encoders). A stream may start without a clear code (the table starts initialized).
/// </description></item>
/// <item><description>
/// Malformed codes are <see cref="InvalidImageContentException"/>: a code above the next table code, the next table code
/// itself (the <c>KwKwK</c> case) when no previous code exists, and a table code as the first code after a clear.
/// </description></item>
/// <item><description>
/// After the end code, the remaining input (padding bits and any further sub-blocks) is ignored, like the reference decoders.
/// After the last pixel of the image, <see cref="ReadTrailer"/> ignores everything, as browsers do: a string longer than the
/// pixels that remain is cut, and data codes after the last pixel (a stream that is reset and goes on) are not decoded.
/// Whether the decoded index count matches the image is checked by the caller (<see cref="GifDecoder"/>).
/// </description></item>
/// <item><description>
/// Working memory: the code table (per code: its length, its last 1 to 4 indices and the code of the rest of its string) and
/// one 4096-byte string buffer, rented once from the operation scope as <see cref="AllocationKind.DecoderState"/> and reused
/// by every image.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class GifLzwDecoder : IDisposable
{
    /// <summary>The number of codes of a full table (12-bit codes).</summary>
    public const int MaxCodes = 4096;

    private const int MaxCodeSize = 12;

    // Each code keeps the last 1 to 4 indices of its string (a chunk) and the code whose string is the rest, a multiple
    // of 4 indices long: a string is written 4 indices per step instead of one
    private const int ChunkLength = sizeof(uint);
    private const int HeadOffset = 0;
    private const int LengthOffset = MaxCodes * sizeof(ushort);
    private const int ChunkOffset = LengthOffset + (MaxCodes * sizeof(ushort));
    private const int StringOffset = ChunkOffset + (MaxCodes * ChunkLength);
    private const int StateLength = StringOffset + MaxCodes;

    private PooledBuffer? _state;
    private int _clearCode;
    private int _endCode;
    private int _minimumCodeSize;
    private int _codeSize;
    private int _codeMask;
    private int _nextCode;
    private int _previousCode;
    private ulong _bits;
    private int _bitCount;
    private int _pendingStart;
    private int _pendingLength;

    /// <summary>Allocates the code table.</summary>
    /// <exception cref="ImageResourceLimitException">The table exceeds the live-allocation limit.</exception>
    public GifLzwDecoder(AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _state = scope.Rent(StateLength, AllocationKind.DecoderState, clear: false);
    }

    /// <summary>Gets a value indicating whether the end code was read.</summary>
    public bool IsEnded { get; private set; }

    /// <summary>Prepares the decoder for a new image datastream.</summary>
    /// <param name="minimumCodeSize">The LZW minimum code size of the image (1 to 11; validated by the walker).</param>
    public void Reset(int minimumCodeSize)
    {
        if (minimumCodeSize is < 1 or > 11)
            throw new ArgumentOutOfRangeException(nameof(minimumCodeSize), minimumCodeSize, "The LZW minimum code size must be 1 to 11.");

        _minimumCodeSize = minimumCodeSize;
        _clearCode = 1 << minimumCodeSize;
        _endCode = _clearCode + 1;
        _bits = 0;
        _bitCount = 0;
        _pendingStart = 0;
        _pendingLength = 0;
        IsEnded = false;

        // Literal codes are one-byte strings: their chunk is the index itself
        var state = GetState();
        var lengths = unsafe(MemoryMarshal.Cast<byte, ushort>(state.Slice(LengthOffset, MaxCodes * sizeof(ushort))));
        var chunks = state.Slice(ChunkOffset, MaxCodes * ChunkLength);
        for (var code = 0; code < _clearCode; code++)
        {
            lengths[code] = 1;
            chunks[code * ChunkLength] = (byte)code;
        }

        ResetTable();
    }

    /// <summary>
    /// Decodes color indices into <paramref name="output"/> until it is full, the input is exhausted, or the end code is read.
    /// Consumed input bytes are removed from <paramref name="input"/> (all of it once the end code was read).
    /// </summary>
    /// <returns>The number of indices written.</returns>
    /// <exception cref="InvalidImageContentException">The stream contains an invalid code.</exception>
    public int Decode(ref ReadOnlySpan<byte> input, scoped Span<byte> output)
    {
        if (IsEnded)
        {
            input = default;
            return 0;
        }

        var state = GetState();
        var heads = unsafe(MemoryMarshal.Cast<byte, ushort>(state.Slice(HeadOffset, MaxCodes * sizeof(ushort))));
        var lengths = unsafe(MemoryMarshal.Cast<byte, ushort>(state.Slice(LengthOffset, MaxCodes * sizeof(ushort))));
        var chunkBytes = state.Slice(ChunkOffset, MaxCodes * ChunkLength);
        var chunks = unsafe(MemoryMarshal.Cast<byte, uint>(chunkBytes));
        var buffer = state.Slice(StringOffset, MaxCodes);
        var written = 0;

        // The rest of a string that did not fit in the previous output buffer
        if (_pendingLength > 0)
        {
            var count = Math.Min(_pendingLength, output.Length);
            buffer.Slice(_pendingStart, count).CopyTo(output);
            _pendingStart += count;
            _pendingLength -= count;
            written = count;
            if (_pendingLength > 0)
                return written;
        }

        var bits = _bits;
        var bitCount = _bitCount;
        var offset = 0;
        try
        {
            while (written < output.Length)
            {
                while (bitCount < _codeSize)
                {
                    if (offset == input.Length)
                        return written;

                    bits |= (ulong)input[offset++] << bitCount;
                    bitCount += 8;
                }

                var code = (int)(bits & (uint)_codeMask);
                bits >>= _codeSize;
                bitCount -= _codeSize;
                if (code == _clearCode)
                {
                    ResetTable();
                    continue;
                }

                if (code == _endCode)
                {
                    IsEnded = true;
                    offset = input.Length; // padding bits and later sub-blocks are ignored
                    return written;
                }

                var previous = _previousCode;
                int length;
                byte first;
                scoped Span<byte> target;
                var destination = output[written..];
                if (previous < 0)
                {
                    // First code after a clear (or at the start of the stream): the table holds only the literals
                    if (code > _endCode)
                        throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The GIF LZW code {code} follows a clear code but is not a color index."));

                    destination[0] = (byte)code;
                    written++;
                    _previousCode = code;
                    GrowCodeSize(); // a minimum code size of 1 starts with a full 2-bit code space
                    continue;
                }

                if (code < _clearCode)
                {
                    length = 1;
                    target = destination;
                    first = (byte)code;
                    destination[0] = first;
                }
                else if (code < _nextCode)
                {
                    length = lengths[code];
                    target = length <= destination.Length ? destination : buffer;
                    first = Expand(heads, chunks, chunkBytes, code, target, length);
                }
                else if (code == _nextCode && _nextCode < MaxCodes)
                {
                    // KwKwK: the string of the previous code followed by its own first index
                    length = lengths[previous] + 1;
                    target = length <= destination.Length ? destination : buffer;
                    first = Expand(heads, chunks, chunkBytes, previous, target, length - 1);
                    target[length - 1] = first;
                }
                else
                {
                    throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The GIF LZW code {code} is not defined (the next table code is {_nextCode})."));
                }

                if (_nextCode < MaxCodes)
                {
                    // The string of the previous code followed by the first index of this one: the index extends the chunk of
                    // the previous code, or starts a chunk after it when that chunk is full
                    var next = _nextCode;
                    var previousLength = lengths[previous];
                    var chunkCount = ((previousLength - 1) & (ChunkLength - 1)) + 1;
                    if (chunkCount < ChunkLength)
                    {
                        chunks[next] = chunks[previous];
                        chunkBytes[(next * ChunkLength) + chunkCount] = first;
                        heads[next] = heads[previous];
                    }
                    else
                    {
                        chunkBytes[next * ChunkLength] = first;
                        heads[next] = (ushort)previous;
                    }

                    lengths[next] = (ushort)(previousLength + 1);
                    _nextCode++;
                    GrowCodeSize();
                }

                _previousCode = code;
                if (target == destination)
                {
                    written += length;
                }
                else
                {
                    // The string is longer than the space left: keep the remainder for the next call
                    var count = destination.Length;
                    buffer[..count].CopyTo(destination);
                    written += count;
                    _pendingStart = count;
                    _pendingLength = length - count;
                }
            }

            return written;
        }
        finally
        {
            _bits = bits;
            _bitCount = bitCount;
            input = input[offset..];
        }
    }

    /// <summary>
    /// Reads what follows the last pixel of the image: nothing more is decoded, as browsers do. The codes that follow (an end
    /// code, clear codes, data codes or the rest of a string longer than the image) and the further sub-blocks are ignored.
    /// </summary>
    public void ReadTrailer(ref ReadOnlySpan<byte> input)
    {
        IsEnded = true;
        _pendingLength = 0;
        input = default;
    }

    public void Dispose()
    {
        _state?.Dispose();
        _state = null;
    }

    /// <summary>
    /// Writes the string of <paramref name="code"/> (<paramref name="length"/> indices) at the start of <paramref name="target"/>
    /// and returns its first index. The indices after the string may be overwritten (they are not written yet).
    /// </summary>
    private static byte Expand(ReadOnlySpan<ushort> heads, ReadOnlySpan<uint> chunks, ReadOnlySpan<byte> chunkBytes, int code, Span<byte> target, int length)
    {
        // The last chunk holds 1 to 4 indices; every chunk before it is full
        var end = length - (((length - 1) & (ChunkLength - 1)) + 1);
        if (end <= target.Length - ChunkLength)
        {
            var last = chunks[code];
            unsafe
            {
                MemoryMarshal.Write(target.Slice(end, ChunkLength), in last);
            }
        }
        else
        {
            chunkBytes.Slice(code * ChunkLength, length - end).CopyTo(target[end..]);
        }

        while (end > 0)
        {
            code = heads[code];
            end -= ChunkLength;
            var chunk = chunks[code];
            unsafe
            {
                MemoryMarshal.Write(target.Slice(end, ChunkLength), in chunk);
            }
        }

        return target[0];
    }

    private void GrowCodeSize()
    {
        if (_nextCode >= 1 << _codeSize && _codeSize < MaxCodeSize)
        {
            _codeSize++;
            _codeMask = (1 << _codeSize) - 1;
        }
    }

    private void ResetTable()
    {
        _codeSize = _minimumCodeSize + 1;
        _codeMask = (1 << _codeSize) - 1;
        _nextCode = _endCode + 1;
        _previousCode = -1;
    }

    private Span<byte> GetState() => (_state ?? throw new ObjectDisposedException(nameof(GifLzwDecoder))).RawBuffer.AsSpan(0, StateLength);

    private static InvalidImageContentException Invalid(string message) => new(message, ImageFormat.Gif);
}
