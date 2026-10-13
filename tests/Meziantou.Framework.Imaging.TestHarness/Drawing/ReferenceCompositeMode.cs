namespace Meziantou.Framework.Imaging.TestHarness.Drawing;

/// <summary>The compositing operators of Porter and Duff, for the reference compositor.</summary>
public enum ReferenceCompositeMode
{
    /// <summary>The paint over the pixel.</summary>
    SourceOver,

    /// <summary>The paint only.</summary>
    Source,

    /// <summary>The pixel over the paint.</summary>
    DestinationOver,

    /// <summary>The paint where the pixel is.</summary>
    SourceIn,

    /// <summary>The pixel where the paint is.</summary>
    DestinationIn,

    /// <summary>The paint where the pixel is not.</summary>
    SourceOut,

    /// <summary>The pixel where the paint is not.</summary>
    DestinationOut,

    /// <summary>The paint over the pixel, where the pixel is.</summary>
    SourceAtop,

    /// <summary>The pixel over the paint, where the paint is.</summary>
    DestinationAtop,

    /// <summary>The paint and the pixel, where the other one is not.</summary>
    Xor,

    /// <summary>Nothing.</summary>
    Clear,
}
