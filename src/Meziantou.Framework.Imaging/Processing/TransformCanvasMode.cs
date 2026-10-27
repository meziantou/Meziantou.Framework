namespace Meziantou.Framework.Imaging;

/// <summary>Selects the canvas of a transformed image (<see cref="TransformOptions.CanvasMode"/>).</summary>
public enum TransformCanvasMode
{
    /// <summary>
    /// The smallest canvas that holds the whole transformed image, with the transformed image centered in it: the
    /// translation of the transformation does not matter. This is the default.
    /// </summary>
    Expand = 0,

    /// <summary>The canvas keeps the size of the image; what the transformation moves outside of it is cut.</summary>
    Keep = 1,

    /// <summary>The canvas has the size of <see cref="TransformOptions.CanvasSize"/>.</summary>
    Explicit = 2,
}
