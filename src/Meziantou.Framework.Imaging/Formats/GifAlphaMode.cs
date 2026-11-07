namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Controls how the GIF encoder maps alpha to GIF's single fully transparent palette entry.</summary>
public enum GifAlphaMode
{
    /// <summary>Pixels whose alpha is below <see cref="GifEncoder.AlphaThreshold"/> become fully transparent; the others become fully opaque. This is the default.</summary>
    Threshold = 0,

    /// <summary>Composites every pixel over <see cref="GifEncoder.BackgroundColor"/>, producing a fully opaque image.</summary>
    Flatten = 1,
}
