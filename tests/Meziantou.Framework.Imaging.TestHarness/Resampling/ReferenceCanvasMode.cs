namespace Meziantou.Framework.Imaging.TestHarness.Resampling;

/// <summary>The canvas of a reference transformation.</summary>
public enum ReferenceCanvasMode
{
    /// <summary>The smallest canvas that holds the transformed image, centered in it.</summary>
    Expand,

    /// <summary>The size of the source.</summary>
    Keep,

    /// <summary>A given size.</summary>
    Explicit,
}
