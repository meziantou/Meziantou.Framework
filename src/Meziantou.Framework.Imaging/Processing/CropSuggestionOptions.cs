namespace Meziantou.Framework.Imaging;

/// <summary>Immutable options of <see cref="ImageProcessingExtensions.SuggestCrop(Image, int, int, CropSuggestionOptions?, CancellationToken)"/>.</summary>
public sealed class CropSuggestionOptions
{
    /// <summary>Gets what the crop keeps. Defaults to <see cref="CropEnergy.Edges"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="CropEnergy"/>.</exception>
    public CropEnergy Energy
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The crop energy is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the size of the crop as a fraction of the largest rectangle of the aspect ratio that fits in the image, greater
    /// than 0 and at most 1. Defaults to 1: the crop removes only what the aspect ratio requires. A smaller scale also
    /// zooms on the content.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than 0 and at most 1.</exception>
    public double Scale
    {
        get;
        init
        {
            if (!(value is > 0 and <= 1))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The scale must be greater than 0 and at most 1.");

            field = value;
        }
    } = 1;
}
