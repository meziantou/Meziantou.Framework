namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Encodes the base level of a face or an array element and the mipmap levels generated from it, one row of blocks per
/// <see cref="Step"/> call, into buffers charged to the scope of the writer.
/// </summary>
/// <remarks>
/// The levels are encoded from the largest one; a level is generated from the previous one (<see cref="TextureMipmaps"/>)
/// once the previous one is encoded, so that only two levels of working samples are alive at a time.
/// </remarks>
internal sealed class TextureSurfaceEncoder : IDisposable
{
    private readonly TextureTarget _target;
    private readonly bool _thorough;
    private readonly bool _srgbFilter;
    private readonly AllocationScope _scope;
    private readonly PooledBuffer?[] _levels;
    private TexturePixels? _pixels;
    private int _level;
    private int _blockRow;

    /// <summary>Starts the encoding of a surface.</summary>
    /// <param name="pixels">The working samples of the base level; owned by the encoder from now on.</param>
    /// <param name="target">The format.</param>
    /// <param name="levelCount">The number of levels to encode: 1 for the base level only.</param>
    /// <param name="thorough">Whether the block encoders search thoroughly.</param>
    /// <param name="srgbFilter">Whether the integer samples are sRGB-encoded, and the levels averaged in linear light.</param>
    /// <param name="scope">The scope charged for the encoded levels and the working samples.</param>
    public TextureSurfaceEncoder(TexturePixels pixels, TextureTarget target, int levelCount, bool thorough, bool srgbFilter, AllocationScope scope)
    {
        _target = target;
        _thorough = thorough;
        _srgbFilter = srgbFilter;
        _scope = scope;
        _levels = new PooledBuffer?[levelCount];
        _pixels = pixels;
        try
        {
            _levels[0] = Rent(pixels.Width, pixels.Height);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Gets a value indicating whether every level is encoded.</summary>
    public bool IsComplete => _level >= _levels.Length;

    /// <summary>Encodes one row of blocks, or generates the next level.</summary>
    /// <returns><see langword="true"/> when every level is encoded.</returns>
    public bool Step(CancellationToken cancellationToken)
    {
        if (IsComplete)
            return true;

        var pixels = _pixels!;
        var rows = TextureTarget.GetBlockCount(pixels.Height, _target.BlockHeight);
        var rowLength = TextureBlockWriter.GetBlockRowLength(_target, pixels.Width);
        TextureBlockWriter.EncodeBlockRow(pixels, _target, _thorough, _blockRow, _levels[_level]!.Span.Slice(_blockRow * rowLength, rowLength));
        _blockRow++;
        if (_blockRow < rows)
            return false;

        _blockRow = 0;
        _level++;
        if (IsComplete)
        {
            _pixels = null;
            pixels.Dispose();
            return true;
        }

        var next = TextureMipmaps.Downsample(pixels, _srgbFilter, _scope, cancellationToken);
        pixels.Dispose();
        _pixels = next;
        _levels[_level] = Rent(next.Width, next.Height);
        return false;
    }

    /// <summary>Encodes every remaining level.</summary>
    public void Complete(CancellationToken cancellationToken)
    {
        while (!Step(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    /// <summary>Takes the encoded levels, from the largest one: the caller disposes them.</summary>
    public PooledBuffer[] TakeLevels()
    {
        if (!IsComplete)
            throw new InvalidOperationException("The surface is not encoded.");

        var levels = new PooledBuffer[_levels.Length];
        for (var i = 0; i < levels.Length; i++)
        {
            levels[i] = _levels[i]!;
            _levels[i] = null;
        }

        return levels;
    }

    public void Dispose()
    {
        _pixels?.Dispose();
        _pixels = null;
        for (var i = 0; i < _levels.Length; i++)
        {
            _levels[i]?.Dispose();
            _levels[i] = null;
        }
    }

    private PooledBuffer Rent(int width, int height)
    {
        var length = _target.GetSurfaceLength(width, height);
        if (length > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(_scope.Limits);

        return _scope.Rent((int)length, AllocationKind.Temporary, clear: false);
    }
}
