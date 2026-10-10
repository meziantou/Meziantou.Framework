namespace Meziantou.Framework.Imaging.TestHarness.Convolution;

/// <summary>The pixels a reference convolution reads outside the image (independent of the library types).</summary>
public enum ReferenceEdgeMode
{
    /// <summary>The nearest edge pixel.</summary>
    Clamp,

    /// <summary>The image reflected about its borders (the edge pixel is repeated).</summary>
    Mirror,

    /// <summary>The image tiled.</summary>
    Wrap,

    /// <summary>Nothing: outside pixels contribute zero.</summary>
    Zero,
}
