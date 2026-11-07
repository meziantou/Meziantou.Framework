using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A line drawn with a text (underline, strikethrough, overline), as a rectangle from the top-left corner of the layout.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct DecorationRectangle(double X, double Y, double Width, double Height, int Brush);
