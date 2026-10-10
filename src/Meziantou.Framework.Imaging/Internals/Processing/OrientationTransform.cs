using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// One of the eight exact pixel permutations of a rectangle (the dihedral group of the square): the identity, the three
/// quarter-turn rotations, the two mirrors and the two diagonal reflections. It is defined by the mapping from destination
/// to source coordinates: <c>(u, v) = Transpose ? (dy, dx) : (dx, dy)</c>, then
/// <c>sx = FlipX ? sourceWidth - 1 - u : u</c> and <c>sy = FlipY ? sourceHeight - 1 - v : v</c>.
/// </summary>
/// <remarks>
/// Rotations are clockwise. The transform needed to display stored pixels upright for an EXIF
/// orientation is <see cref="ForOrientation(ExifOrientation)"/>.
/// </remarks>
internal readonly record struct OrientationTransform(bool Transpose, bool FlipX, bool FlipY)
{
    public static OrientationTransform Identity => default;

    /// <summary>Gets a value indicating whether the transform leaves every pixel in place.</summary>
    public bool IsIdentity => !Transpose && !FlipX && !FlipY;

    /// <summary>Gets the transform of a clockwise rotation.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not defined.</exception>
    public static OrientationTransform ForRotation(RotateMode mode) => mode switch
    {
        RotateMode.None => Identity,

        // dest(x, y) = src(y, H - 1 - x): the bottom-left source pixel becomes the top-left one
        RotateMode.Rotate90 => new(Transpose: true, FlipX: false, FlipY: true),
        RotateMode.Rotate180 => new(Transpose: false, FlipX: true, FlipY: true),

        // dest(x, y) = src(W - 1 - y, x): the top-right source pixel becomes the top-left one
        RotateMode.Rotate270 => new(Transpose: true, FlipX: true, FlipY: false),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "The rotation is not valid."),
    };

    /// <summary>Gets the transform of a mirror.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not defined.</exception>
    public static OrientationTransform ForFlip(FlipMode mode) => mode switch
    {
        FlipMode.Horizontal => new(Transpose: false, FlipX: true, FlipY: false),
        FlipMode.Vertical => new(Transpose: false, FlipX: false, FlipY: true),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "The flip mode is not valid."),
    };

    /// <summary>Gets the transform that makes pixels stored with <paramref name="orientation"/> display upright (orientation 1).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="orientation"/> is not defined.</exception>
    public static OrientationTransform ForOrientation(ExifOrientation orientation) => orientation switch
    {
        ExifOrientation.TopLeft => Identity,
        ExifOrientation.TopRight => ForFlip(FlipMode.Horizontal),
        ExifOrientation.BottomRight => ForRotation(RotateMode.Rotate180),
        ExifOrientation.BottomLeft => ForFlip(FlipMode.Vertical),

        // 5: stored row r is displayed as column r and stored column c as row c (transpose)
        ExifOrientation.LeftTop => new(Transpose: true, FlipX: false, FlipY: false),
        ExifOrientation.RightTop => ForRotation(RotateMode.Rotate90),

        // 7: stored row r is displayed as column W' - 1 - r and stored column c as row H' - 1 - c (transverse)
        ExifOrientation.RightBottom => new(Transpose: true, FlipX: true, FlipY: true),
        ExifOrientation.LeftBottom => ForRotation(RotateMode.Rotate270),
        _ => throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "The orientation must be between 1 and 8."),
    };

    /// <summary>Gets the size of the result for a source of <paramref name="source"/> size.</summary>
    public Size GetOutputSize(Size source) => Transpose ? new Size(source.Height, source.Width) : source;
}
