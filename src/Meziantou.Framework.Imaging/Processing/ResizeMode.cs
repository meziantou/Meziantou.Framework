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
}
