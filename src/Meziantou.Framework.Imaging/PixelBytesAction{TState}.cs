namespace Meziantou.Framework.Imaging;

/// <summary>A callback receiving scoped access to the raw pixel bytes of a frame and caller state.</summary>
/// <typeparam name="TState">The type of the state.</typeparam>
/// <param name="pixels">The accessor, valid only until the callback returns.</param>
/// <param name="state">The state passed by the caller.</param>
public delegate void PixelBytesAction<TState>(PixelBytesAccessor pixels, TState state)
    where TState : allows ref struct;
