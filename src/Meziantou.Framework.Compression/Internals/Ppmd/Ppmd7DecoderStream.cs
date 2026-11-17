using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Ppmd;

/// <summary>
/// Decodes the 7z PPMd coder (03 04 01): the PPMd var.H model with the 7z range coder. The 7z properties (5 bytes: the order,
/// then the memory size as a little-endian UInt32) are parsed by the caller.
/// </summary>
internal sealed class Ppmd7DecoderStream : ReadOnlyStreamBase
{
    // A valid stream never needs more bytes than the encoder wrote; a few zeros past the end tolerate a short flush
    private const int MaxExtraBytes = 4;

    private readonly Stream _input;
    private readonly bool _leaveOpen;
    private readonly int _order;
    private readonly uint _memorySize;
    private Ppmd7Model? _model;
    private Ppmd7zRangeDecoder _rangeDecoder;
    private long _remaining;
    private bool _finished;
    private bool _failed;
    private bool _disposed;

    /// <param name="input">The packed stream.</param>
    /// <param name="order">The model order, from 2 to 64.</param>
    /// <param name="memorySize">The size of the model memory in bytes.</param>
    /// <param name="outputLength">The number of bytes to decode, or -1 to decode up to the end marker.</param>
    /// <param name="maxMemoryUsage">The maximum memory size the decoder accepts.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="input"/> open when the stream is disposed.</param>
    /// <exception cref="InvalidDataException">The order or the memory size is not valid, or the memory size exceeds <paramref name="maxMemoryUsage"/>.</exception>
    public Ppmd7DecoderStream(Stream input, int order, uint memorySize, long outputLength, long maxMemoryUsage, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfLessThan(outputLength, -1);
        Ppmd7Model.ValidateParameters(order, memorySize, maxMemoryUsage);

        _input = input;
        _leaveOpen = leaveOpen;
        _order = order;
        _memorySize = memorySize;
        _remaining = outputLength;
        _finished = outputLength == 0;
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_failed)
            throw new InvalidDataException("The PPMd data is corrupted.");

        if (_finished || buffer.IsEmpty)
            return 0;

        if (_model is null)
        {
            if (!_rangeDecoder.Initialize(new PpmdStreamByteSource(_input)))
            {
                _failed = true;
                throw new InvalidDataException("The PPMd data is not valid.");
            }

            _model = new Ppmd7Model(_memorySize);
            _model.Init(_order);
        }

        var count = buffer.Length;
        if (_remaining >= 0 && count > _remaining)
        {
            count = (int)_remaining;
        }

        var model = _model;
        var read = 0;
        while (read < count)
        {
            var symbol = model.DecodeSymbol(ref _rangeDecoder);
            if (symbol < 0)
            {
                if (symbol == -1 && _remaining < 0)
                {
                    _finished = true;
                    break;
                }

                // The model stopped in the middle of a symbol: it cannot be continued
                _failed = true;
                throw new InvalidDataException(symbol == -1 ? "The PPMd data ends before the expected length." : "The PPMd data is corrupted.");
            }

            buffer[read++] = (byte)symbol;
        }

        if (_rangeDecoder.ExtraBytes > MaxExtraBytes)
        {
            _failed = true;
            throw new InvalidDataException("Unexpected end of the PPMd data.");
        }

        if (_remaining >= 0)
        {
            _remaining -= read;
            if (_remaining == 0)
            {
                _finished = true;
            }
        }

        return read;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            if (!_leaveOpen)
            {
                _input.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
