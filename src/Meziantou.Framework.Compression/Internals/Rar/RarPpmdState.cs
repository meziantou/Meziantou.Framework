using Meziantou.Framework.Compression.Internals.Ppmd;

namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>The PPMd (variant H) model of the PPMd blocks of RAR 2.9, which reads the bytes of the range decoder from the bits of the LZ data.</summary>
internal sealed class RarPpmdState
{
    private readonly long _maxMemoryUsage;
    private readonly PpmdRarDecoder _decoder = new();
    private readonly InputSource _source;

    public RarPpmdState(RarBitReader input, long maxMemoryUsage)
    {
        _maxMemoryUsage = maxMemoryUsage;
        _source = new InputSource(input);
    }

    /// <summary>Starts a PPMd block: (re)creates the model when <paramref name="resetModel"/> is set, and initializes the range decoder.</summary>
    /// <exception cref="InvalidDataException">The block is invalid, or its model needs more memory than the maximum memory usage.</exception>
    public void Initialize(int maxOrder, int memorySize, bool resetModel)
    {
        if (!_decoder.Initialize(_source, maxOrder, memorySize, resetModel, _maxMemoryUsage))
            throw new InvalidDataException("The PPMd block of the compressed data is invalid.");
    }

    /// <summary>Decodes a byte.</summary>
    /// <exception cref="InvalidDataException">The data are corrupted.</exception>
    public int DecodeSymbol()
    {
        var symbol = _decoder.DecodeSymbol();
        if (symbol < 0)
            throw new InvalidDataException("The PPMd data of the compressed data are corrupted.");

        return symbol;
    }

    private sealed class InputSource(RarBitReader input) : PpmdByteSource
    {
        public override int ReadByte() => input.ReadByteOrEnd();
    }
}
