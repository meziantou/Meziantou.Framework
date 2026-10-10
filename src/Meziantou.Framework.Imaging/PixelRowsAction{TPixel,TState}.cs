namespace Meziantou.Framework.Imaging;

/// <summary>A callback receiving scoped access to the pixel rows of a frame and caller state.</summary>
/// <typeparam name="TPixel">The pixel type.</typeparam>
/// <typeparam name="TState">The type of the state.</typeparam>
/// <param name="pixels">The accessor, valid only until the callback returns.</param>
/// <param name="state">The state passed by the caller.</param>
public delegate void PixelRowsAction<TPixel, TState>(PixelAccessor<TPixel> pixels, TState state)
    where TPixel : unmanaged
    where TState : allows ref struct;
