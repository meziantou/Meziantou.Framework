namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The prepared state of a morphological filter (<see cref="MorphologyFilter"/>): the structuring element, the edge rule,
/// the sample keys of the pixel format and the scratch. One plan serves every frame of an image.
/// </summary>
/// <remarks>
/// The scratch is rented from the scope of the image before any pixel changes: the keys of the rows a pass keeps (for a
/// rectangle, three blocks of <c>2 RadiusY + 1</c> filtered rows; for an ellipse, a ring of as many padded rows), of the
/// rows read above and below the frame (the halos), and of the running extrema of a row; and, for the top-hat
/// transforms, a copy of the frame.
/// </remarks>
internal sealed class MorphologyPlan : IDisposable
{
    private PooledBuffer? _keys;
    private PixelStorage? _original;

    /// <summary>Initializes a new instance of the <see cref="MorphologyPlan"/> class.</summary>
    /// <param name="element">The structuring element.</param>
    /// <param name="edgeMode">The pixels read outside the frame.</param>
    /// <param name="operation">The operation.</param>
    /// <param name="format">The pixel format the kernel works in (<see cref="ProcessingBridge.GetKernelFormat"/>).</param>
    /// <param name="size">The size of the frames.</param>
    public MorphologyPlan(StructuringElement element, ConvolutionEdgeMode edgeMode, MorphologyOperation operation, PixelFormat format, Size size)
    {
        Element = element;
        EdgeMode = edgeMode;
        Operation = operation;
        Format = format;
        Size = size;
        Channels = PixelFormats.GetComponentCount(format);
        KeyBytes = format switch
        {
            PixelFormat.RgbaFloat => 4,
            PixelFormat.Rgba64 or PixelFormat.Gray16 or PixelFormat.Rgb48 or PixelFormat.RgbaHalf => 2,
            PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgb24 or PixelFormat.Gray8 => 1,
            _ => throw new NotSupportedException($"The pixel format '{format}' is not supported."),
        };
    }

    public StructuringElement Element { get; }

    public ConvolutionEdgeMode EdgeMode { get; }

    public MorphologyOperation Operation { get; }

    /// <summary>Gets the pixel format the kernel works in.</summary>
    public PixelFormat Format { get; }

    /// <summary>Gets the size of the frames.</summary>
    public Size Size { get; }

    /// <summary>Gets the number of samples per pixel (alpha included).</summary>
    public int Channels { get; }

    /// <summary>Gets the size of a sample key: 1 for 8-bit samples, 2 for 16-bit and half-precision ones, 4 for single-precision ones (<see cref="MedianFilter.ToKey"/>).</summary>
    public int KeyBytes { get; }

    /// <summary>Gets the number of keys of a row of the frame.</summary>
    public int RowLength => Size.Width * Channels;

    /// <summary>Gets the number of keys of a row padded by <see cref="StructuringElement.RadiusX"/> pixels on each side.</summary>
    public int PaddedLength => (Size.Width + (2 * Element.RadiusX)) * Channels;

    /// <summary>Gets the number of keys of the scratch (<see cref="GetKeys{TKey}"/>).</summary>
    public long KeyCount
    {
        get
        {
            long padded = PaddedLength;
            long row = RowLength;
            long k = Element.Height;
            long halos = 2L * Element.RadiusY;

            // A rectangle: the halos and three blocks of filtered rows, a padded row and its running extrema.
            // An ellipse: a ring of padded rows, the padded halos, the running extrema and the output row.
            return Element.IsRectangle ? ((halos + (3 * k)) * row) + (3 * padded) : ((k + halos + 2) * padded) + row;
        }
    }

    /// <summary>Gets the number of bytes of scratch storage rented by <see cref="Prepare"/> (pool capacity, as charged).</summary>
    public long ScratchBytes { get; private set; }

    /// <summary>Gets the copy of the frame of a top-hat transform, rented by <see cref="Prepare"/>.</summary>
    public PixelStorage Original => _original ?? throw new InvalidOperationException("The plan has no copy of the frame.");

    /// <summary>Rents the scratch. Called once per operation, before any pixel changes.</summary>
    /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit; the plan must still be disposed.</exception>
    public void Prepare(StorageOwner owner)
    {
        var scope = owner.Scope;
        var before = scope.LiveBytes;
        if (!CheckedSizes.TryMultiply(KeyCount, KeyBytes, out var bytes))
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        _keys = ResampleWeights.Rent<byte>(scope, bytes);
        if (Operation is MorphologyOperation.TopHat or MorphologyOperation.BlackHat)
        {
            _original = owner.Allocate(Size.Width, Size.Height, PixelFormats.GetBytesPerPixel(Format), options: null, AllocationKind.Temporary);
        }

        ScratchBytes = scope.LiveBytes - before;
    }

    /// <summary>Gets the scratch keys.</summary>
    public Span<TKey> GetKeys<TKey>()
        where TKey : unmanaged
        => unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, TKey>((_keys ?? throw new ObjectDisposedException(nameof(MorphologyPlan))).Span))[..checked((int)KeyCount)];

    /// <summary>Gets the image row read for row <paramref name="y"/>, which may be outside the image, or -1 when it reads zeros.</summary>
    public int MapRow(int y) => ConvolutionPlan.MapIndex(y, Size.Height, EdgeMode);

    /// <summary>Gets the image column read for column <paramref name="x"/>, which may be outside the image, or -1 when it reads zeros.</summary>
    public int MapColumn(int x) => ConvolutionPlan.MapIndex(x, Size.Width, EdgeMode);

    public void Dispose()
    {
        _keys?.Dispose();
        _keys = null;
        _original?.Dispose();
        _original = null;
    }
}
