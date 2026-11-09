namespace Meziantou.Framework.Imaging;

/// <summary>Selects how the pixels of an OpenEXR deep image are decoded (<see cref="OpenExrDecodingOptions.DeepData"/>).</summary>
/// <remarks>
/// A deep image stores a list of samples per pixel, each at its own depth (the <c>Z</c> channel, and <c>ZBack</c> for a
/// sample that covers a range of depths), so that images can be merged by depth. An image of the library has one color per
/// pixel: decoding a deep image composites its samples, and the depths are lost.
/// </remarks>
public enum OpenExrDeepDataMode
{
    /// <summary>
    /// Deep images are rejected with an <see cref="UnsupportedImageFeatureException"/>, since their depths would be lost.
    /// This is the default.
    /// </summary>
    Reject = 0,

    /// <summary>
    /// The samples of each pixel are sorted by depth and composited front to back with the <em>over</em> operation, as
    /// OpenEXR flattens deep data: the image has the color and the alpha of the composite. The channels <c>Z</c> and
    /// <c>ZBack</c> give the depths and are not decoded as colors.
    /// </summary>
    Flatten = 1,
}
