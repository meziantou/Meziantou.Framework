namespace Meziantou.Framework.Imaging;

/// <summary>What <see cref="ImageProcessingExtensions.SuggestCrop(Image, int, int, CropSuggestionOptions?, CancellationToken)"/> keeps in the crop.</summary>
public enum CropEnergy
{
    /// <summary>
    /// The details: the differences of luma and of alpha between neighboring pixels. Textured and detailed areas are kept,
    /// flat backgrounds (sky, studio walls) are cut, and so is a transparent background, whose outline with the subject is
    /// kept. This is the default.
    /// </summary>
    Edges = 0,

    /// <summary>
    /// The colors that stand out: the frequency-tuned saliency of Achanta et al. (2009), the distance in Oklab between the
    /// slightly blurred color of every pixel and the mean color of the frame. A subject that differs from its surroundings
    /// is kept even when it is smooth.
    /// </summary>
    Saliency = 1,
}
