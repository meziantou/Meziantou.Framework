namespace Meziantou.Framework.Imaging;

/// <summary>A callback receiving scoped access to the raw pixel bytes of a frame.</summary>
/// <param name="pixels">The accessor, valid only until the callback returns.</param>
public delegate void PixelBytesAction(PixelBytesAccessor pixels);
