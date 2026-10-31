using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>An area of a reference grid: the columns <c>X0</c> to <c>X1 - 1</c> and the rows <c>Y0</c> to <c>Y1 - 1</c>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Jpeg2000Area(long X0, long Y0, long X1, long Y1)
{
    public bool IsEmpty => X0 >= X1 || Y0 >= Y1;

    public Jpeg2000Area Intersect(Jpeg2000Area other) => new(Math.Max(X0, other.X0), Math.Max(Y0, other.Y0), Math.Min(X1, other.X1), Math.Min(Y1, other.Y1));

    /// <summary>Gets the area on a grid whose cells are 2^<paramref name="shift"/> times larger (equation B-14).</summary>
    public Jpeg2000Area Reduce(int shift) => new(Jpeg2000TileGeometry.CeilingShift(X0, shift), Jpeg2000TileGeometry.CeilingShift(Y0, shift), Jpeg2000TileGeometry.CeilingShift(X1, shift), Jpeg2000TileGeometry.CeilingShift(Y1, shift));
}
