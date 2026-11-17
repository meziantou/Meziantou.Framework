namespace Meziantou.Framework.Imaging.Barcodes.Internal;

/// <summary>
/// The binary images the readers search, in order, until one of them gives a symbol: each classification of the pixels,
/// then the same classification inverted for the symbols printed light on a dark background.
/// </summary>
internal static class BinarizationPasses
{
    /// <summary>
    /// Runs a locator on the binary images of an image until it finds a symbol.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="tryInverted">Whether the inverted images are searched too.</param>
    /// <param name="locate">Searches a binary image and returns whether the results have a symbol.</param>
    /// <param name="cancellationToken">A token to cancel the reading.</param>
    public static void Run(Image image, bool tryInverted, Func<BinaryImage, bool> locate, CancellationToken cancellationToken)
    {
        using var luminance = BinaryImage.GetLuminance(image);
        Run(luminance, tryInverted, locate, cancellationToken);
    }

    /// <summary>Runs a locator on the binary images of the luminance of an image until it finds a symbol.</summary>
    /// <param name="luminance">The luminance of the image.</param>
    /// <param name="tryInverted">Whether the inverted images are searched too.</param>
    /// <param name="locate">Searches a binary image and returns whether the results have a symbol.</param>
    /// <param name="cancellationToken">A token to cancel the reading.</param>
    /// <param name="includeSauvola">Whether the threshold of Sauvola is tried last.</param>
    public static void Run(Image<Gray8> luminance, bool tryInverted, Func<BinaryImage, bool> locate, CancellationToken cancellationToken, bool includeSauvola = true)
    {
        // The local threshold first: it follows the shadows and the gradients of photographs. The threshold of the whole
        // image is for the symbols whose modules are larger than the blocks of the local threshold, and Sauvola for the
        // remaining images of uneven lighting.
        foreach (var method in (ReadOnlySpan<BinarizationMethod>)[BinarizationMethod.LocalBlocks, BinarizationMethod.Otsu, BinarizationMethod.Sauvola])
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!includeSauvola && method is BinarizationMethod.Sauvola)
                continue;

            var binary = Create(luminance, method, cancellationToken);
            if (locate(binary))
                return;

            if (tryInverted && locate(binary.Invert()))
                return;
        }
    }

    private static BinaryImage Create(Image<Gray8> luminance, BinarizationMethod method, CancellationToken cancellationToken)
    {
        if (method is BinarizationMethod.LocalBlocks)
            return BinaryImage.CreateLocalBlocks(luminance, cancellationToken);

        if (method is BinarizationMethod.Otsu)
            return BinaryImage.Create(luminance, new ThresholdOptions { Method = ThresholdMethod.Otsu }, cancellationToken);

        // The window spans a few modules of any reasonable size
        var window = Math.Clamp((Math.Min(luminance.Width, luminance.Height) / 8) | 1, 15, 201);
        return BinaryImage.Create(luminance, new ThresholdOptions { Method = ThresholdMethod.Sauvola, WindowSize = window }, cancellationToken);
    }

    private enum BinarizationMethod
    {
        LocalBlocks,
        Otsu,
        Sauvola,
    }
}
