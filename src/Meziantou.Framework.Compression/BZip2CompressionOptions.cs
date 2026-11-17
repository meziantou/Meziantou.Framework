namespace Meziantou.Framework.Compression;

/// <summary>Options to compress data with <see cref="BZip2Stream"/>.</summary>
public sealed class BZip2CompressionOptions
{
    /// <summary>
    /// Gets or sets the size of the blocks, in hundreds of kB, from 1 to 9 (the options <c>-1</c> to <c>-9</c> of bzip2). Larger
    /// blocks compress better and need more memory to compress and decompress. The default is 9.
    /// </summary>
    public int BlockSize
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 9);
            field = value;
        }
    } = 9;
}
