using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A paint color converted to the pixel format of a frame: the color samples in storage order (a single sample for
/// grayscale formats) and the opacity of the paint, all at the precision of the format. The opacity exists whatever the
/// format: a format without alpha is still painted with translucent colors.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct DrawingColor(ushort C0, ushort C1, ushort C2, ushort Alpha);
