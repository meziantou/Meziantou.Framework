namespace Meziantou.Framework.Imaging;

/// <summary>
/// Selects how <see cref="ImageProcessingExtensions.Threshold(Image, ThresholdOptions?, CancellationToken)"/> chooses the
/// threshold of each pixel.
/// </summary>
public enum ThresholdMethod
{
    /// <summary>
    /// One threshold for the whole frame, chosen by Otsu's method from the histogram of its luminance
    /// (<see cref="ImageHistogram.GetOtsuThreshold"/>): the level that maximizes the variance between the two classes. This
    /// is the default.
    /// </summary>
    Otsu = 0,

    /// <summary>One threshold for the whole frame: <see cref="ThresholdOptions.Level"/>.</summary>
    Fixed = 1,

    /// <summary>
    /// An adaptive threshold: the mean luminance of the <see cref="ThresholdOptions.WindowSize"/> square centered on the
    /// pixel, minus <see cref="ThresholdOptions.Offset"/>. It follows uneven lighting, such as the shadows of a scanned or
    /// photographed page.
    /// </summary>
    Mean = 2,

    /// <summary>
    /// An adaptive threshold for documents (Sauvola and Pietikäinen, 2000): <c>m * (1 + k * (s / R - 1))</c>, where <c>m</c>
    /// and <c>s</c> are the mean and the standard deviation of the luminance in the <see cref="ThresholdOptions.WindowSize"/>
    /// square centered on the pixel, <c>k</c> is <see cref="ThresholdOptions.SauvolaK"/> and <c>R</c> is
    /// <see cref="ThresholdOptions.SauvolaR"/>. Where the window has little contrast, the threshold falls below the mean, so
    /// a blank background stays blank.
    /// </summary>
    Sauvola = 3,
}
