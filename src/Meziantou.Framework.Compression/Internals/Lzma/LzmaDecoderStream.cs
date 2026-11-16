// A port of the LZMA decoder of the LZMA SDK by Igor Pavlov, and of xz-utils (liblzma), which are in the public domain.
using System.Buffers.Binary;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Lzma;

/// <summary>
/// Decodes a raw LZMA stream (the data of the 7z and zip LZMA methods, of the .lzma and .lz formats). The data ends after
/// <c>outputLength</c> bytes, or at the end marker when the length is unknown (-1). With a known length, an end marker right
/// after the data is accepted (zip LZMA with the EOS flag) and is not read.
/// </summary>
internal sealed class LzmaDecoderStream : ReadOnlyStreamBase
{
    private readonly LzmaInputBuffer _input;
    private readonly bool _ownsInput;
    private readonly LzmaDecoder _decoder;
    private readonly long _outputLength;
    private long _produced;
    private int _pending;
    private bool _initialized;
    private bool _finished;
    private bool _disposed;

    /// <param name="input">The compressed data.</param>
    /// <param name="properties">The 5-byte LZMA properties: the lc/lp/pb byte, then the dictionary size (little-endian).</param>
    /// <param name="outputLength">The length of the decompressed data, or -1 to decode until the end marker.</param>
    /// <param name="leaveOpen">Whether to leave <paramref name="input"/> open when the stream is disposed.</param>
    public LzmaDecoderStream(Stream input, ReadOnlySpan<byte> properties, long outputLength, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (properties.Length < 5)
            throw new InvalidDataException("The LZMA properties are too short.");

        if (!LzmaDecoder.TryDecodeProperties(properties[0], out var lc, out var lp, out var pb))
            throw new InvalidDataException("The LZMA properties are invalid.");

        _input = new LzmaInputBuffer(input, leaveOpen);
        _ownsInput = true;
        _outputLength = outputLength;
        _decoder = new LzmaDecoder(BinaryPrimitives.ReadUInt32LittleEndian(properties[1..]), outputLength);
        _decoder.SetProperties(lc, lp, pb);
        _decoder.ResetState();
    }

    /// <summary>Decodes the LZMA data at the position of a buffer that the caller owns (the .lzma and .lz readers).</summary>
    internal LzmaDecoderStream(LzmaInputBuffer input, int lc, int lp, int pb, uint dictionarySize, long outputLength, long maxMemoryUsage)
    {
        _input = input;
        _outputLength = outputLength;
        _decoder = new LzmaDecoder(dictionarySize, outputLength, maxMemoryUsage);
        _decoder.SetProperties(lc, lp, pb);
        _decoder.ResetState();
    }

    /// <summary>Gets a value indicating whether all the data was decoded and read.</summary>
    internal bool IsFinished => _finished && _pending == _decoder.WindowPosition;

    /// <summary>Gets the number of decompressed bytes returned so far.</summary>
    internal long Produced => _produced;

    public override long Length => _outputLength >= 0 ? _outputLength : throw new NotSupportedException();

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty)
            return 0;

        while (true)
        {
            var available = _decoder.WindowPosition - _pending;
            if (available > 0)
            {
                var count = Math.Min(available, buffer.Length);
                _decoder.Window.AsSpan(_pending, count).CopyTo(buffer);
                _pending += count;
                _produced += count;
                return count;
            }

            if (_finished)
                return 0;

            if (_outputLength >= 0 && _produced >= _outputLength)
            {
                _finished = true;
                return 0;
            }

            if (!_initialized)
            {
                if (!_input.Fill(5))
                    throw new InvalidDataException("Unexpected end of data.");

                var position = _input.Position;
                _decoder.InitializeRangeDecoder(_input.Buffer, ref position);
                _input.Position = position;
                _initialized = true;
            }

            DecodeSome(buffer.Length);
        }
    }

    private void DecodeSome(int requested)
    {
        if (_decoder.WindowPosition == _decoder.WindowCapacity)
        {
            _decoder.MakeRoom();
            _pending = _decoder.WindowPosition;
        }

        var position = _decoder.WindowPosition;
        long limit = Math.Min(_decoder.WindowCapacity, position + (long)Math.Max(requested, 1 << 16));
        if (_outputLength >= 0)
        {
            limit = Math.Min(limit, position + (_outputLength - _produced));
        }

        if (_input.Available < LzmaInputBuffer.Padding)
        {
            _input.Fill(LzmaInputBuffer.Padding);
        }

        var inputLimit = _input.IsEndOfStream ? _input.End : _input.End - LzmaInputBuffer.Padding;
        var inputPosition = _input.Position;
        if (inputPosition > inputLimit)
            throw new InvalidDataException("Unexpected end of data.");

        var endMarker = _decoder.Decode(_input.Buffer, ref inputPosition, inputLimit, (int)limit);
        _input.Position = inputPosition;
        if (inputPosition > _input.End)
            throw new InvalidDataException("Unexpected end of data.");

        if (endMarker)
        {
            if (_outputLength >= 0 && _produced + (_decoder.WindowPosition - _pending) != _outputLength)
                throw new InvalidDataException("The LZMA data ends before the declared length.");

            _finished = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _decoder.Dispose();
            if (_ownsInput)
            {
                _input.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
