using System.Runtime.InteropServices;

namespace Meziantou.Framework.Diagrams.Layout;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct LayoutPoint(double X, double Y);
