using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Ppmd;

/// <summary>
/// Decodes zip method 98: PPMd var.I rev. 1 with the restore methods 0 (restart) and 1 (cut off). The 2-byte header of the
/// zip data (order, memory size and restore method) is parsed by the caller.
/// </summary>
internal sealed class Ppmd8DecoderStream : ReadOnlyStreamBase
{
    // A valid stream never needs more bytes than the encoder wrote; a few zeros past the end tolerate a short flush
    private const int MaxExtraBytes = 4;

    private readonly Stream _input;
    private readonly bool _leaveOpen;
    private readonly int _order;
    private readonly uint _memorySize;
    private readonly int _restoreMethod;
    private Ppmd8Model? _model;
    private long _remaining;
    private bool _finished;
    private bool _failed;
    private bool _disposed;

    /// <param name="input">The compressed data, after the 2-byte header.</param>
    /// <param name="order">The model order, from 2 to 16.</param>
    /// <param name="memorySize">The size of the model memory in bytes.</param>
    /// <param name="restoreMethod">0 to restart the model when its memory is full, 1 to cut it off.</param>
    /// <param name="outputLength">The number of bytes to decode, or -1 to decode up to the end marker.</param>
    /// <param name="maxMemoryUsage">The maximum memory size the decoder accepts.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="input"/> open when the stream is disposed.</param>
    /// <exception cref="InvalidDataException">
    /// The order, the memory size or the restore method is not valid, or the memory size exceeds <paramref name="maxMemoryUsage"/>.
    /// </exception>
    public Ppmd8DecoderStream(Stream input, int order, uint memorySize, int restoreMethod, long outputLength, long maxMemoryUsage, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfLessThan(outputLength, -1);
        Ppmd8Model.ValidateParameters(order, memorySize, restoreMethod, maxMemoryUsage);

        _input = input;
        _leaveOpen = leaveOpen;
        _order = order;
        _memorySize = memorySize;
        _restoreMethod = restoreMethod;
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
            var model = new Ppmd8Model(_memorySize);
            if (!model.InitRangeDecoder(new PpmdStreamByteSource(_input)))
            {
                _failed = true;
                throw new InvalidDataException("The PPMd data is not valid.");
            }

            model.Init(_order, _restoreMethod);
            _model = model;
        }

        var count = buffer.Length;
        if (_remaining >= 0 && count > _remaining)
        {
            count = (int)_remaining;
        }

        var read = 0;
        while (read < count)
        {
            var symbol = _model.DecodeSymbol();
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

        if (_model.ExtraBytes > MaxExtraBytes)
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
