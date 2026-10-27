using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>An inclusive rectangle of integer pixel coordinates (a <c>box2i</c> attribute).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ExrBox(int MinX, int MinY, int MaxX, int MaxY)
{
    public long Width => (long)MaxX - MinX + 1;

    public long Height => (long)MaxY - MinY + 1;

    public bool Contains(ExrBox other) => other.MinX >= MinX && other.MinY >= MinY && other.MaxX <= MaxX && other.MaxY <= MaxY;
}
