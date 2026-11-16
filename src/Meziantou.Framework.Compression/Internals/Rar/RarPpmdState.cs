namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>The PPMd (variant H) model of the PPMd blocks of RAR 2.9, which reads the bytes of the range decoder from the bits of the LZ data.</summary>
internal sealed class RarPpmdState
{
    private readonly long _maxMemoryUsage;
#if RAR_PPMD
    private readonly Ppmd.PpmdRarDecoder _decoder = new();
    private readonly InputSource _source;
#endif

    public RarPpmdState(RarBitReader input, long maxMemoryUsage)
    {
        _maxMemoryUsage = maxMemoryUsage;
#if RAR_PPMD
        _source = new InputSource(input);
#else
        _ = input;
#endif
    }

    /// <summary>Starts a PPMd block: (re)creates the model when <paramref name="resetModel"/> is set, and initializes the range decoder.</summary>
    public void Initialize(int maxOrder, int memorySize, bool resetModel)
    {
#if RAR_PPMD
        if (!_decoder.Initialize(_source, maxOrder, memorySize, resetModel, _maxMemoryUsage))
            throw new InvalidDataException("The PPMd block of the compressed data is invalid.");
#else
        // TODO(compression): PPMd is wired once the PpmdRarDecoder of the PPMd codec is merged (define RAR_PPMD)
        _ = maxOrder;
        _ = memorySize;
        _ = resetModel;
        _ = _maxMemoryUsage;
        throw new NotSupportedException("The PPMd blocks of RAR 2.9 are not supported yet.");
#endif
    }

    /// <summary>Decodes a byte.</summary>
    /// <exception cref="InvalidDataException">The data are corrupted.</exception>
    public int DecodeSymbol()
    {
#if RAR_PPMD
        var symbol = _decoder.DecodeSymbol();
        if (symbol < 0)
            throw new InvalidDataException("The PPMd data of the compressed data are corrupted.");

        return symbol;
#else
        throw new NotSupportedException("The PPMd blocks of RAR 2.9 are not supported yet.");
#endif
    }

#if RAR_PPMD
    private sealed class InputSource(RarBitReader input) : Ppmd.PpmdByteSource
    {
        public override int ReadByte() => input.ReadByteOrEnd();
    }
#endif
}
