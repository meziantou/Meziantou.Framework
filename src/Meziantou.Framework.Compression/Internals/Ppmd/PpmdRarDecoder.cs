namespace Meziantou.Framework.Compression.Internals.Ppmd;

/// <summary>
/// Decodes the PPMd blocks of RAR 2.9/4.x: the PPMd var.H model with the carry-less range decoder of RAR (Ppmd7a_RangeDec and
/// Ppmd7a_DecodeSymbol of 7-Zip). The RAR decoder parses the block header (flags, memory size, escape character) and drives it.
/// </summary>
internal sealed class PpmdRarDecoder
{
    // A valid block never needs more bytes than the encoder wrote; a few zeros past the end tolerate a short flush
    private const int MaxExtraBytes = 4;

    private Ppmd7Model? _model;
    private uint _memorySize;
    private PpmdCarrylessRangeDecoder _rangeDecoder;
    private bool _modelValid;
    private bool _rangeDecoderInitialized;

    /// <summary>
    /// Restarts the model when <paramref name="resetModel"/> is <see langword="true"/>, allocating its memory when its size
    /// changes, then initializes the range decoder by reading 4 bytes from <paramref name="source"/>.
    /// </summary>
    /// <param name="source">The bytes of the block.</param>
    /// <param name="maxOrder">The model order, from 2 to 64 (RAR computes it from the flags of the block).</param>
    /// <param name="memorySize">The size of the model memory in bytes: (MaxMB + 1) &lt;&lt; 20 for RAR.</param>
    /// <param name="resetModel"><see langword="true"/> to start a new model; <see langword="false"/> to continue the current one.</param>
    /// <param name="maxMemoryUsage">The maximum memory size the decoder accepts.</param>
    /// <returns>
    /// <see langword="false"/> when the start of the range decoder is not valid, or when there is no model to continue: none was
    /// started, or <see cref="DecodeSymbol"/> reported an error since (as unrar, the model is dropped after an error).
    /// </returns>
    /// <exception cref="InvalidDataException">
    /// <paramref name="resetModel"/> is <see langword="true"/> and the order or the memory size is not valid, or the memory size
    /// exceeds <paramref name="maxMemoryUsage"/>.
    /// </exception>
    public bool Initialize(PpmdByteSource source, int maxOrder, int memorySize, bool resetModel, long maxMemoryUsage)
    {
        ArgumentNullException.ThrowIfNull(source);

        _rangeDecoderInitialized = false;
        if (resetModel)
        {
            _modelValid = false;
            if (memorySize < 0)
                throw new InvalidDataException($"The PPMd memory size {memorySize} is not supported.");

            Ppmd7Model.ValidateParameters(maxOrder, (uint)memorySize, maxMemoryUsage);
            if (_model is null || _memorySize != (uint)memorySize)
            {
                _model = null; // Let the previous memory be collected before allocating the new one
                _model = new Ppmd7Model((uint)memorySize);
                _memorySize = (uint)memorySize;
            }

            _model.Init(maxOrder);
            _modelValid = true;
        }
        else if (!_modelValid)
        {
            return false;
        }

        if (!_rangeDecoder.Initialize(source))
            return false;

        _rangeDecoderInitialized = true;
        return true;
    }

    /// <summary>
    /// Decodes a byte (0 to 255), or returns -1 on a data error, or when <see cref="Initialize"/> did not succeed. After an
    /// error, the next block must reset the model.
    /// </summary>
    public int DecodeSymbol()
    {
        if (!_rangeDecoderInitialized || _model is null)
            return -1;

        var symbol = _model.DecodeSymbol(ref _rangeDecoder);
        if (symbol < 0 || _rangeDecoder.ExtraBytes > MaxExtraBytes)
        {
            // The model stopped in the middle of a symbol: it cannot be continued
            _rangeDecoderInitialized = false;
            _modelValid = false;
            return -1;
        }

        return symbol;
    }
}
