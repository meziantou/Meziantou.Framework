using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Ppmd;

/// <summary>
/// Encodes the 7z PPMd coder (03 04 01): the PPMd var.H model with the 7z range coder, without an end marker, as 7-Zip does.
/// Disposing the stream flushes the range encoder.
/// </summary>
internal sealed class Ppmd7EncoderStream : WriteOnlyStreamBase
{
    private static readonly byte[] DefaultOrders = [3, 4, 4, 5, 5, 6, 8, 16, 24, 32];

    private readonly Stream _output;
    private readonly bool _leaveOpen;
    private readonly Ppmd7Model _model;
    private Ppmd7zRangeEncoder _rangeEncoder;
    private bool _disposed;

    /// <param name="output">The stream the packed data is written to.</param>
    /// <param name="order">The model order, from 2 to 64.</param>
    /// <param name="memorySize">The size of the model memory in bytes.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="output"/> open when the stream is disposed.</param>
    public Ppmd7EncoderStream(Stream output, int order, uint memorySize, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentOutOfRangeException.ThrowIfLessThan(order, Ppmd7Model.MinOrder);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(order, Ppmd7Model.MaxOrder);
        ArgumentOutOfRangeException.ThrowIfLessThan(memorySize, Ppmd7Model.MinMemorySize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(memorySize, Ppmd7Model.MaxMemorySize);

        _output = output;
        _leaveOpen = leaveOpen;
        _model = new Ppmd7Model(memorySize);
        _model.Init(order);
        _rangeEncoder.Initialize(output, new byte[16384]);
    }

    /// <summary>
    /// Gets the order and the memory size 7-Zip uses for a compression level (0 to 9, -1 for the default level 5): the memory
    /// size is 2^(level + 19) bytes (16 MiB at level 5) and the orders are 3, 4, 4, 5, 5, 6, 8, 16, 24 and 32.
    /// </summary>
    public static (int Order, uint MemorySize) GetDefaultParameters(int level)
    {
        if (level < 0)
        {
            level = 5;
        }
        else if (level > 9)
        {
            level = 9;
        }

        return (DefaultOrders[level], 1u << (level + 19));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var model = _model;
        foreach (var value in buffer)
        {
            model.EncodeSymbol(ref _rangeEncoder, value);
        }
    }

    /// <summary>Writes the bytes the range encoder completed to the output stream. The last bytes are written when the stream is disposed.</summary>
    public override void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _rangeEncoder.FlushBuffer();
        _output.Flush();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            try
            {
                _rangeEncoder.FlushData();
                _rangeEncoder.FlushBuffer();
                _output.Flush();
            }
            finally
            {
                if (!_leaveOpen)
                {
                    _output.Dispose();
                }
            }
        }

        base.Dispose(disposing);
    }
}
