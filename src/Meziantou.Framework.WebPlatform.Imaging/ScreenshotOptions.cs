using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Imaging;

/// <summary>Options of a screenshot (see <see cref="ImagingScreenshotExtensions"/>).</summary>
public sealed class ScreenshotOptions
{
    /// <summary>
    /// Gets the number of pixels of the screenshot per CSS pixel. The default is null: the
    /// <see cref="ViewportOptions.DevicePixelRatio"/> of the browsing context (a screenshot of a 800x600 viewport at a device pixel
    /// ratio of 2 is 1600x1200 pixels). Changing it does not change the layout (the media queries still see the device pixel
    /// ratio of the viewport): the painting is scaled.
    /// </summary>
    public double? DevicePixelRatio { get; init; }
}
