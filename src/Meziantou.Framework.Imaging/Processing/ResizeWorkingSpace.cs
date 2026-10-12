namespace Meziantou.Framework.Imaging;

/// <summary>Selects the numeric space in which resampling arithmetic is performed.</summary>
public enum ResizeWorkingSpace
{
    /// <summary>Filters the stored (encoded, gamma-compressed) sample values directly. This is the default.</summary>
    Encoded = 0,

    /// <summary>
    /// Converts samples to linear light with the transfer function of the image, filters, and converts back. The function
    /// is sRGB (IEC 61966-2-1) unless the image has a CICP color description (<see cref="Metadata.ImageMetadata.Cicp"/>)
    /// and no ICC profile: the samples are then decoded with the function of its transfer characteristics (Rec. ITU-T
    /// H.273, table 3), whatever its color primaries, and PQ and HLG samples are filtered in the light their function
    /// decodes to. Unspecified transfer characteristics are sRGB; reserved ones are rejected. Images carrying an ICC
    /// profile that is not known to be sRGB-compatible are rejected, as no ICC transform is applied implicitly: convert
    /// them first with <see cref="ImageProcessingExtensions.ConvertColorProfile"/>. Images whose
    /// <see cref="Metadata.ImageMetadata.TransferFunction"/> is <see cref="Metadata.ColorTransferFunction.Linear"/>, or whose
    /// transfer characteristics are linear, already store linear light: their samples are filtered directly.
    /// </summary>
    /// <remarks>The name is historical: the working space is the linear light of the image's own transfer function.</remarks>
    LinearSrgb = 1,
}
