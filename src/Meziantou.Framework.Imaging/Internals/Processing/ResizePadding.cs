using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The canvas of a padding resize: its size (the target size) and where the resized image lies on it.</summary>
/// <param name="CanvasSize">The size of the result.</param>
/// <param name="Offset">The top-left corner of the resized image on the canvas.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ResizePadding(Size CanvasSize, Point Offset);
