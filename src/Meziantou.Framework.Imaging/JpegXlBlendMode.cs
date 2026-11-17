namespace Meziantou.Framework.Imaging;

/// <summary>
/// How a layer of a JPEG XL image (<see cref="JpegXlLayer"/>) is combined with the layers below it: the blend modes of the
/// format (ISO/IEC 18181-1, section F.2). The color channels and the alpha channel use the same mode.
/// </summary>
/// <remarks>
/// A decoder blends samples as numbers from 0 to 1 (1 being the largest value of the bit depth) in the color space of the
/// image, and rounds the result to the bit depth; the result is clamped to the range of the samples. The modes that weight
/// by alpha use the alpha channel of the image: in an image without alpha, <see cref="Blend"/> replaces and
/// <see cref="AlphaWeightedAdd"/> adds.
/// </remarks>
public enum JpegXlBlendMode
{
    /// <summary>The layer replaces the pixels below it, its alpha included: <c>result = layer</c>.</summary>
    Replace = 0,

    /// <summary>The samples of the layer, alpha included, are added to the ones below it: <c>result = below + layer</c>.</summary>
    Add = 1,

    /// <summary>
    /// The layer is drawn over the pixels below it, weighted by its alpha (the "over" operator of straight alpha):
    /// <c>alpha = layerAlpha + belowAlpha * (1 - layerAlpha)</c>, and each color the weighted mean of the two colors.
    /// </summary>
    Blend = 2,

    /// <summary>
    /// The colors of the layer, multiplied by its alpha, are added to the ones below it:
    /// <c>result = below + layer * layerAlpha</c>. The alpha below is kept.
    /// </summary>
    AlphaWeightedAdd = 3,

    /// <summary>The samples, alpha included, are multiplied by those of the layer: <c>result = below * layer</c>.</summary>
    Multiply = 4,
}
