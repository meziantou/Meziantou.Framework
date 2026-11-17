using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Ppmd;

/// <summary>
/// Encodes zip method 98: PPMd var.I rev. 1 with the restore methods 0 (restart) and 1 (cut off). The data end with the end
/// marker, so that a decoder that does not know the length stops there. The 2-byte header of the zip data (order, memory
/// size and restore method) is written by the caller. Disposing the stream writes the end marker and flushes the range encoder.
/// </summary>
internal sealed class Ppmd8EncoderStream : WriteOnlyStreamBase
{
    private readonly Stream _output;
    private readonly bool _leaveOpen;
    private readonly Ppmd8Model _model;
    private PpmdCarrylessRangeEncoder _rangeEncoder;
    private bool _disposed;

    /// <param name="output">The stream the packed data is written to.</param>
    /// <param name="order">The model order, from 2 to 16.</param>
    /// <param name="memorySize">The size of the model memory in bytes.</param>
    /// <param name="restoreMethod">0 to restart the model when its memory is full, 1 to cut it off.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="output"/> open when the stream is disposed.</param>
    public Ppmd8EncoderStream(Stream output, int order, uint memorySize, int restoreMethod, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentOutOfRangeException.ThrowIfLessThan(order, Ppmd8Model.MinOrder);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(order, Ppmd8Model.MaxOrder);
        ArgumentOutOfRangeException.ThrowIfLessThan(memorySize, Ppmd8Model.MinMemorySize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(memorySize, Ppmd8Model.MaxMemorySize);
        if (restoreMethod is not Ppmd8Model.RestoreMethodRestart and not Ppmd8Model.RestoreMethodCutOff)
            throw new ArgumentOutOfRangeException(nameof(restoreMethod), restoreMethod, "The restore method must be 0 (restart) or 1 (cut off).");

        _output = output;
        _leaveOpen = leaveOpen;
        _model = new Ppmd8Model(memorySize);
        _model.Init(order, restoreMethod);
        _rangeEncoder.Initialize(output, new byte[16384]);
    }

    /// <summary>Gets the model, to inspect its restorations.</summary>
    internal Ppmd8Model Model => _model;

    /// <summary>
    /// Gets the parameters 7-Zip uses for a compression level (1 to 9, -1 for the default level 5; 0 is level 1): the order
    /// is 3 + level, the memory size 2^(level - 1) MiB, and the restore method cuts the model off from level 7. When the
    /// length of the data is known, the memory size is reduced, as 7-Zip does, to the smallest power of two of MiB that is
    /// at least 16 times the length.
    /// </summary>
    public static (int Order, uint MemorySizeInMiB, int RestoreMethod) GetDefaultParameters(int level, long knownLength = -1)
    {
        if (level < 0)
        {
            level = 5;
        }
        else if (level is 0)
        {
            level = 1;
        }
        else if (level > 9)
        {
            level = 9;
        }

        var memory = 1u << (level - 1);
        if (knownLength >= 0)
        {
            for (var m = 1u; m < memory; m <<= 1)
            {
                if (knownLength <= (m << 20) / 16)
                {
                    memory = m;
                    break;
                }
            }
        }

        return (3 + level, memory, level < 7 ? Ppmd8Model.RestoreMethodRestart : Ppmd8Model.RestoreMethodCutOff);
    }

    /// <summary>Gets the 2-byte header of zip method 98: the order minus 1 in bits 0-3, the memory size in MiB minus 1 in bits 4-11, the restore method in bits 12-15.</summary>
    public static ushort GetZipHeader(int order, uint memorySizeInMiB, int restoreMethod) => (ushort)((order - 1) | ((int)(memorySizeInMiB - 1) << 4) | (restoreMethod << 12));

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
                _model.EncodeSymbol(ref _rangeEncoder, -1);
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
