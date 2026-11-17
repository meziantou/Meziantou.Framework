using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A layer of a still JPEG XL image, as <see cref="JpegXlDecoder"/> decodes it in layer mode.</summary>
/// <param name="Image">The samples of the layer, before they are blended; the receiver disposes it.</param>
/// <param name="Offset">The position of the layer on the image.</param>
/// <param name="BlendMode">How the layer is combined with the layers below it.</param>
/// <param name="Name">The name of the layer, or <see langword="null"/>.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct JpegXlDecodedLayer(Image Image, Point Offset, JpegXlBlendMode BlendMode, string? Name);
