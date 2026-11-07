namespace Meziantou.Framework.Imaging;

/// <summary>A callback receiving scoped access to the pixel rows of a frame.</summary>
/// <typeparam name="TPixel">The pixel type.</typeparam>
/// <param name="pixels">The accessor, valid only until the callback returns.</param>
public delegate void PixelRowsAction<TPixel>(PixelAccessor<TPixel> pixels)
    where TPixel : unmanaged;
