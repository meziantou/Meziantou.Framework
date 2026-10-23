namespace Meziantou.Framework.Pdf;

/// <summary>The attributes of a transparency group (ISO 32000-2, 11.6.6): a form whose content is composited as one object.</summary>
public sealed class PdfTransparencyGroup
{
    /// <summary>Gets or initializes the blending color space of the group; <see langword="null"/> for the color space of the parent group.</summary>
    public PdfDeviceColorSpace? ColorSpace { get; init; }

    /// <summary>Gets or initializes whether the group is isolated: composited on a transparent backdrop, not on the backdrop of the page.</summary>
    public bool Isolated { get; init; }

    /// <summary>Gets or initializes whether the group is a knockout group: each object is composited with the initial backdrop of the group, not with the objects before it.</summary>
    public bool Knockout { get; init; }
}
