using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A color of a wide row (<see cref="PixelFormat.Rgba32"/> or <see cref="PixelFormat.Rgba64"/> samples), without its alpha.</summary>
/// <param name="R">The red sample.</param>
/// <param name="G">The green sample.</param>
/// <param name="B">The blue sample.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct PngWideColor(ushort R, ushort G, ushort B);
