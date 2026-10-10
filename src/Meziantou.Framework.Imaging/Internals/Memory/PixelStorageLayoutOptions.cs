namespace Meziantou.Framework.Imaging.Internals;

/// <summary>How a <see cref="PixelStorage"/> splits its rows into slabs. Layout never changes visible row contents.</summary>
internal sealed class PixelStorageLayoutOptions
{
    /// <summary>The default target slab size (4 MiB): rows are grouped into slabs of at most this many bytes when more than one row fits.</summary>
    public const int DefaultTargetSlabBytes = 4 * 1024 * 1024;

    /// <summary>Gets the default layout: tightly packed rows grouped into slabs of at most 4 MiB.</summary>
    public static PixelStorageLayoutOptions Default { get; } = new();

    /// <summary>Gets the alignment, in bytes, of the distance between consecutive rows of a slab (1 = tightly packed). Padding is never visible.</summary>
    public int RowAlignment
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 4096);
            field = value;
        }
    } = 1;

    /// <summary>Gets the target slab size, in bytes. A slab always holds at least one row, so a single row larger than this value gets its own slab.</summary>
    public int TargetSlabBytes
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = DefaultTargetSlabBytes;
}
