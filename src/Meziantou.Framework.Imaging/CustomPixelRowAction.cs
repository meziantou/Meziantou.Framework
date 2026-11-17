namespace Meziantou.Framework.Imaging;

/// <summary>Processes one row of the pixels of a frame, converted to a custom pixel type (<see cref="ICustomPixel{TSelf}"/>).</summary>
/// <typeparam name="TPixel">The custom pixel type.</typeparam>
/// <param name="row">The pixels of the row, which the callback may change. It must not retain the span.</param>
/// <param name="y">The index of the row, from the top.</param>
public delegate void CustomPixelRowAction<TPixel>(Span<TPixel> row, int y)
    where TPixel : unmanaged;
