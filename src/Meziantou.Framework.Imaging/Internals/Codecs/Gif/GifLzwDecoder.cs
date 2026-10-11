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
/// After the last pixel of the image, <see cref="ReadTrailer"/> accepts only clear codes and the end code, or the end of the
/// datastream (a missing end code: the unused bits of the last byte are padding). Whether the decoded index count matches the
/// image is checked by the caller (<see cref="GifDecoder"/>).
/// </description></item>
/// <item><description>
/// Working memory: the code table (prefix, suffix and length per code) and one 4096-byte string buffer, rented once from the
/// operation scope as <see cref="AllocationKind.DecoderState"/> and reused by every image.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class GifLzwDecoder : IDisposable
{
    /// <summary>The number of codes of a full table (12-bit codes).</summary>
    public const int MaxCodes = 4096;

    private const int MaxCodeSize = 12;
    private const int PrefixOffset = 0;
    private const int LengthOffset = MaxCodes * sizeof(ushort);
    private const int SuffixOffset = LengthOffset + (MaxCodes * sizeof(ushort));
    private const int StringOffset = SuffixOffset + MaxCodes;
    private const int StateLength = StringOffset + MaxCodes;
    private const string TooManyPixels = "The GIF image data decodes to more pixels than the image rectangle.";

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
    private int _trailerBytes;
    private bool _paddingCode;

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
        _trailerBytes = 0;
        _paddingCode = false;
        IsEnded = false;

        // Literal codes are one-byte strings; their suffix is the index itself
        var state = GetState();
        var lengths = unsafe(MemoryMarshal.Cast<byte, ushort>(state.Slice(LengthOffset, MaxCodes * sizeof(ushort))));
        var suffixes = state.Slice(SuffixOffset, MaxCodes);
        for (var code = 0; code < _clearCode; code++)
        {
            lengths[code] = 1;
            suffixes[code] = (byte)code;
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
        var prefixes = unsafe(MemoryMarshal.Cast<byte, ushort>(state.Slice(PrefixOffset, MaxCodes * sizeof(ushort))));
        var lengths = unsafe(MemoryMarshal.Cast<byte, ushort>(state.Slice(LengthOffset, MaxCodes * sizeof(ushort))));
        var suffixes = state.Slice(SuffixOffset, MaxCodes);
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

                if (code < _nextCode)
                {
                    length = lengths[code];
                    target = length <= destination.Length ? destination : buffer;
                    first = Expand(prefixes, suffixes, code, target[..length]);
                }
                else if (code == _nextCode && _nextCode < MaxCodes)
                {
                    // KwKwK: the string of the previous code followed by its own first index
                    length = lengths[previous] + 1;
                    target = length <= destination.Length ? destination : buffer;
                    first = Expand(prefixes, suffixes, previous, target[..(length - 1)]);
                    target[length - 1] = first;
                }
                else
                {
                    throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The GIF LZW code {code} is not defined (the next table code is {_nextCode})."));
                }

                if (_nextCode < MaxCodes)
                {
                    prefixes[_nextCode] = (ushort)previous;
                    suffixes[_nextCode] = first;
                    lengths[_nextCode] = (ushort)(lengths[previous] + 1);
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
    /// Reads the codes that follow the last pixel of the image: only clear codes and the end code may follow, then anything is
    /// ignored. The datastream may also end without an end code; the unused bits of its last byte are padding (a data code
    /// formed from them alone is not an error unless more data follows).
    /// </summary>
    /// <exception cref="InvalidImageContentException">The datastream decodes to more indices than the image (a data code is followed by more data, or a string exceeds the image).</exception>
    public void ReadTrailer(ref ReadOnlySpan<byte> input)
    {
        if (IsEnded)
        {
            input = default;
            return;
        }

        if (_pendingLength > 0 || (_paddingCode && !input.IsEmpty))
            throw Invalid(TooManyPixels);

        var bits = _bits;
        var bitCount = _bitCount;
        var offset = 0;
        try
        {
            while (true)
            {
                while (bitCount < _codeSize)
                {
                    if (offset == input.Length)
                        return;

                    bits |= (ulong)input[offset++] << bitCount;
                    bitCount += 8;
                    _trailerBytes++;
                }

                var code = (int)(bits & (uint)_codeMask);
                bits >>= _codeSize;
                bitCount -= _codeSize;
                if (_paddingCode)
                {
                    // Scanning the rest of the final byte: an end code proves that the data code was not padding
                    if (code == _endCode)
                        throw Invalid(TooManyPixels);

                    continue;
                }

                if (code == _clearCode)
                {
                    ResetTable();
                    continue;
                }

                if (code == _endCode)
                {
                    IsEnded = true;
                    offset = input.Length;
                    return;
                }

                if (_trailerBytes == 0 && offset == input.Length)
                {
                    // Only unused bits of the last byte read so far: padding if the datastream ends here (no end code)
                    _paddingCode = true;
                    continue;
                }

                throw Invalid(TooManyPixels);
            }
        }
        finally
        {
            _bits = bits;
            _bitCount = bitCount;
            input = input[offset..];
        }
    }

    public void Dispose()
    {
        _state?.Dispose();
        _state = null;
    }

    /// <summary>Writes the string of <paramref name="code"/> (exactly <c>target.Length</c> indices) and returns its first index.</summary>
    private static byte Expand(ReadOnlySpan<ushort> prefixes, ReadOnlySpan<byte> suffixes, int code, Span<byte> target)
    {
        for (var i = target.Length - 1; i > 0; i--)
        {
            target[i] = suffixes[code];
            code = prefixes[code];
        }

        // The chain ends at a literal
        target[0] = (byte)code;
        return (byte)code;
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
