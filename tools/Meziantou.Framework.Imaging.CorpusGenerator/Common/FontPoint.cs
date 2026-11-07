using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>A point of a TrueType contour, in font units with the y axis up.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FontPoint(int X, int Y, bool OnCurve = true);
