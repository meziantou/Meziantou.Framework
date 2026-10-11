namespace Meziantou.Framework.Imaging;

/// <summary>Selects the numeric space in which convolution arithmetic is performed.</summary>
public enum ConvolutionWorkingSpace
{
    /// <summary>Filters the stored (encoded, gamma-compressed) sample values directly. This is the default.</summary>
    Encoded = 0,

    /// <summary>
    /// Converts samples from sRGB to linear light, filters, and converts back. The pixels are assumed to be sRGB: images
    /// carrying an ICC profile that is not known to be sRGB-compatible are rejected, as no ICC transform is applied
    /// implicitly: convert them first with <see cref="ImageProcessingExtensions.ConvertColorProfile"/>. Images whose
    /// <see cref="Metadata.ImageMetadata.TransferFunction"/> is <see cref="Metadata.ColorTransferFunction.Linear"/> already store
    /// linear light: their samples are filtered directly.
    /// </summary>
    LinearSrgb = 1,
}
