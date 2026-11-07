namespace Meziantou.Framework.Imaging;

/// <summary>Selects how a three-dimensional lookup table (<see cref="CubeLut"/>) is interpolated between its points.</summary>
public enum LutInterpolation
{
    /// <summary>
    /// Splits each cell of the lattice into six tetrahedra along its main diagonal and blends the four corners of the
    /// tetrahedron of the color: the most accurate on neutral grays, which stay on the diagonal, and the method of color
    /// grading tools and of FFmpeg's <c>lut3d=interp=tetrahedral</c>. This is the default.
    /// </summary>
    Tetrahedral = 0,

    /// <summary>Blends the eight corners of the cell of the color, as FFmpeg's <c>lut3d=interp=trilinear</c>.</summary>
    Trilinear = 1,
}
