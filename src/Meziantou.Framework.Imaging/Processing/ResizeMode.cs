namespace Meziantou.Framework.Imaging;

/// <summary>Controls how the target size of a resize is interpreted.</summary>
public enum ResizeMode
{
    /// <summary>
    /// Preserves the aspect ratio and fits the whole image within the target size, without padding: one dimension equals
    /// the target and the other is smaller or equal. This is the default.
    /// </summary>
    Contain = 0,

    /// <summary>Produces exactly the target size, ignoring the aspect ratio.</summary>
    Stretch = 1,

    /// <summary>Preserves the aspect ratio, covers the target size, and crops the overflow according to <see cref="ResizeOptions.Anchor"/>, producing exactly the target size.</summary>
    Cover = 2,

    /// <summary>
    /// Fits the whole image within the target size as <see cref="Contain"/> does, then pads it to exactly the target size
    /// with <see cref="ResizeOptions.BackgroundColor"/>; the image is placed on the canvas according to
    /// <see cref="ResizeOptions.Anchor"/> (letterboxing).
    /// </summary>
    Pad = 3,

    /// <summary>
    /// Pads the image to exactly the target size like <see cref="Pad"/>, but never enlarges it: an image that already fits
    /// keeps its size and is only placed on the canvas, and a larger one is reduced as by <see cref="Pad"/>. It is
    /// <see cref="Pad"/> with <see cref="ResizeOptions.AllowUpscaling"/> set to <see langword="false"/>.
    /// </summary>
    BoxPad = 4,
}
