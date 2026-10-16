using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// A transformation of space, as the 4x4 matrices of CSS transforms: translations, scalings and rotations in three
/// dimensions, and perspective. A drawing is the plane <c>z = 0</c>; <see cref="ToProjectiveTransform"/> gives what
/// the transformation does to it.
/// </summary>
/// <remarks>
/// <para>
/// A point <c>(x, y, z)</c> becomes <c>(X / W, Y / W, Z / W)</c>, with <c>X = x M11 + y M21 + z M31 + M41</c>,
/// <c>Y = x M12 + y M22 + z M32 + M42</c>, <c>Z = x M13 + y M23 + z M33 + M43</c> and
/// <c>W = x M14 + y M24 + z M34 + M44</c>: the coefficients have the names of the <c>DOMMatrix</c> of the web
/// platform, and of <see cref="AffineTransform"/> in two dimensions. The default value is the identity.
/// </para>
/// <para>
/// The coordinate system is the one of the canvas and of CSS: x to the right, y downward, z toward the viewer. Angles
/// are in degrees and their sines and cosines come from additions, multiplications and divisions only, as for
/// <see cref="AffineTransform"/>: a rotation by a multiple of 90 degrees is exact.
/// </para>
/// <para>
/// Transformations are combined in three dimensions (<see cref="Then"/>), so that what one of them does to z reaches
/// the next one: a rotation around the y axis, then a perspective, shows the side of the plane that comes closer
/// larger. Inside <see cref="Drawing.DrawingCanvas.Transform(Transform3D, Drawing.DrawingAction)"/>, a transformation
/// of a callback started inside another one is combined that way too.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct Transform3D : IEquatable<Transform3D>
{
    private const int Count = 16;

    // Row by row; the default value of the structure is the identity
    private readonly double[]? _values;

    /// <summary>Initializes a new instance of the <see cref="Transform3D"/> struct from its coefficients, row by row.</summary>
    /// <param name="m11">The factor of x in X.</param>
    /// <param name="m12">The factor of x in Y.</param>
    /// <param name="m13">The factor of x in Z.</param>
    /// <param name="m14">The factor of x in W.</param>
    /// <param name="m21">The factor of y in X.</param>
    /// <param name="m22">The factor of y in Y.</param>
    /// <param name="m23">The factor of y in Z.</param>
    /// <param name="m24">The factor of y in W.</param>
    /// <param name="m31">The factor of z in X.</param>
    /// <param name="m32">The factor of z in Y.</param>
    /// <param name="m33">The factor of z in Z.</param>
    /// <param name="m34">The factor of z in W.</param>
    /// <param name="m41">The constant of X: the translation along x.</param>
    /// <param name="m42">The constant of Y: the translation along y.</param>
    /// <param name="m43">The constant of Z: the translation along z.</param>
    /// <param name="m44">The constant of W.</param>
    /// <exception cref="ArgumentException">A coefficient is not a finite number.</exception>
    public Transform3D(
        double m11, double m12, double m13, double m14,
        double m21, double m22, double m23, double m24,
        double m31, double m32, double m33, double m34,
        double m41, double m42, double m43, double m44)
    {
        double[] values = [m11, m12, m13, m14, m21, m22, m23, m24, m31, m32, m33, m34, m41, m42, m43, m44];
        for (var i = 0; i < Count; i++)
        {
            if (!double.IsFinite(values[i]))
                throw new ArgumentException("The value must be a finite number.", GetName(i));
        }

        _values = values;
    }

    private Transform3D(double[] values)
    {
        _values = values;
    }

    /// <summary>Gets the transformation that changes nothing.</summary>
    public static Transform3D Identity => default;

    /// <summary>Gets the factor of x in X.</summary>
    public double M11 => Get(0);

    /// <summary>Gets the factor of x in Y.</summary>
    public double M12 => Get(1);

    /// <summary>Gets the factor of x in Z.</summary>
    public double M13 => Get(2);

    /// <summary>Gets the factor of x in W.</summary>
    public double M14 => Get(3);

    /// <summary>Gets the factor of y in X.</summary>
    public double M21 => Get(4);

    /// <summary>Gets the factor of y in Y.</summary>
    public double M22 => Get(5);

    /// <summary>Gets the factor of y in Z.</summary>
    public double M23 => Get(6);

    /// <summary>Gets the factor of y in W.</summary>
    public double M24 => Get(7);

    /// <summary>Gets the factor of z in X.</summary>
    public double M31 => Get(8);

    /// <summary>Gets the factor of z in Y.</summary>
    public double M32 => Get(9);

    /// <summary>Gets the factor of z in Z.</summary>
    public double M33 => Get(10);

    /// <summary>Gets the factor of z in W.</summary>
    public double M34 => Get(11);

    /// <summary>Gets the constant of X: the translation along x.</summary>
    public double M41 => Get(12);

    /// <summary>Gets the constant of Y: the translation along y.</summary>
    public double M42 => Get(13);

    /// <summary>Gets the constant of Z: the translation along z.</summary>
    public double M43 => Get(14);

    /// <summary>Gets the constant of W.</summary>
    public double M44 => Get(15);

    /// <summary>Gets a value indicating whether the transformation changes nothing.</summary>
    public bool IsIdentity
    {
        get
        {
            for (var i = 0; i < Count; i++)
            {
                if (Get(i) != ((i % 5) == 0 ? 1 : 0))
                    return false;
            }

            return true;
        }
    }

    /// <summary>Converts a transformation of the plane: it moves the points of each plane <c>z = constant</c> in that plane.</summary>
    /// <param name="transform">The transformation of the plane.</param>
    public static implicit operator Transform3D(AffineTransform transform)
        => new([transform.M11, transform.M12, 0, 0, transform.M21, transform.M22, 0, 0, 0, 0, 1, 0, transform.OffsetX, transform.OffsetY, 0, 1]);

    /// <summary>Converts a projective transformation of the plane: z is kept, and divided by <c>W</c> as x and y are.</summary>
    /// <param name="transform">The transformation of the plane.</param>
    public static implicit operator Transform3D(ProjectiveTransform transform)
        => new([transform.M11, transform.M12, 0, transform.M13, transform.M21, transform.M22, 0, transform.M23, 0, 0, 1, 0, transform.M31, transform.M32, 0, transform.M33]);

    /// <summary>Creates a translation.</summary>
    /// <param name="x">The distance along x.</param>
    /// <param name="y">The distance along y.</param>
    /// <param name="z">The distance along z, toward the viewer when positive.</param>
    /// <returns>The transformation.</returns>
    /// <exception cref="ArgumentException">A distance is not a finite number.</exception>
    public static Transform3D CreateTranslation(double x, double y, double z) => new(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, y, z, 1);

    /// <summary>Creates a scaling around the origin.</summary>
    /// <param name="x">The factor along x.</param>
    /// <param name="y">The factor along y.</param>
    /// <param name="z">The factor along z.</param>
    /// <returns>The transformation.</returns>
    /// <exception cref="ArgumentException">A factor is not a finite number.</exception>
    public static Transform3D CreateScale(double x, double y, double z) => new(x, 0, 0, 0, 0, y, 0, 0, 0, 0, z, 0, 0, 0, 0, 1);

    /// <summary>Creates a rotation around the x axis, as <c>rotateX</c> of CSS.</summary>
    /// <param name="degrees">The angle, in degrees: a positive angle brings the bottom of the plane toward the viewer.</param>
    /// <returns>The transformation.</returns>
    /// <exception cref="ArgumentException"><paramref name="degrees"/> is not a finite number.</exception>
    public static Transform3D CreateRotationX(double degrees)
    {
        GetSinCos(degrees, nameof(degrees), out var sin, out var cos);
        return new(1, 0, 0, 0, 0, cos, sin, 0, 0, -sin + 0.0, cos, 0, 0, 0, 0, 1);
    }

    /// <summary>Creates a rotation around the y axis, as <c>rotateY</c> of CSS.</summary>
    /// <param name="degrees">The angle, in degrees: a positive angle moves the right of the plane away from the viewer.</param>
    /// <returns>The transformation.</returns>
    /// <exception cref="ArgumentException"><paramref name="degrees"/> is not a finite number.</exception>
    public static Transform3D CreateRotationY(double degrees)
    {
        GetSinCos(degrees, nameof(degrees), out var sin, out var cos);
        return new(cos, 0, -sin + 0.0, 0, 0, 1, 0, 0, sin, 0, cos, 0, 0, 0, 0, 1);
    }

    /// <summary>Creates a rotation around the z axis, as <c>rotateZ</c> of CSS: the rotation of <see cref="AffineTransform.CreateRotation(double)"/>.</summary>
    /// <param name="degrees">The angle, in degrees. A positive angle turns clockwise on the image.</param>
    /// <returns>The transformation.</returns>
    /// <exception cref="ArgumentException"><paramref name="degrees"/> is not a finite number.</exception>
    public static Transform3D CreateRotationZ(double degrees)
    {
        GetSinCos(degrees, nameof(degrees), out var sin, out var cos);
        return new(cos, sin, 0, 0, -sin + 0.0, cos, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
    }

    /// <summary>Creates a rotation around an axis through the origin, as <c>rotate3d</c> of CSS.</summary>
    /// <param name="axisX">The x component of the direction of the axis.</param>
    /// <param name="axisY">The y component of the direction of the axis.</param>
    /// <param name="axisZ">The z component of the direction of the axis.</param>
    /// <param name="degrees">The angle, in degrees, clockwise when the axis points toward the viewer.</param>
    /// <returns>The transformation; the identity when the direction has no length.</returns>
    /// <exception cref="ArgumentException">A component of the axis, or <paramref name="degrees"/>, is not a finite number.</exception>
    public static Transform3D CreateRotation(double axisX, double axisY, double axisZ, double degrees)
    {
        if (!double.IsFinite(axisX) || !double.IsFinite(axisY) || !double.IsFinite(axisZ))
            throw new ArgumentException("The axis must be finite.", nameof(axisX));

        if (!double.IsFinite(degrees))
            throw new ArgumentException("The value must be a finite number.", nameof(degrees));

        var length = Math.Sqrt((axisX * axisX) + (axisY * axisY) + (axisZ * axisZ));
        if (!(length > 0) || !double.IsFinite(length))
            return Identity;

        var x = axisX / length;
        var y = axisY / length;
        var z = axisZ / length;

        // The matrix of CSS Transforms Level 2, from the sine and the cosine of half the angle
        DeterministicAngle.SinCos(degrees / 360, out var halfSin, out var halfCos);
        var sc = halfSin * halfCos;
        var sq = halfSin * halfSin;
        return new(
            1 - (2 * ((y * y) + (z * z)) * sq),
            2 * ((x * y * sq) + (z * sc)),
            2 * ((x * z * sq) - (y * sc)),
            0,
            2 * ((x * y * sq) - (z * sc)),
            1 - (2 * ((x * x) + (z * z)) * sq),
            2 * ((y * z * sq) + (x * sc)),
            0,
            2 * ((x * z * sq) + (y * sc)),
            2 * ((y * z * sq) - (x * sc)),
            1 - (2 * ((x * x) + (y * y)) * sq),
            0,
            0,
            0,
            0,
            1);
    }

    /// <summary>Creates a perspective, as <c>perspective</c> of CSS: the viewer is at a distance from the plane <c>z = 0</c>, on the z axis.</summary>
    /// <param name="distance">The distance from the viewer to the plane <c>z = 0</c>, in pixels. Must be positive.</param>
    /// <returns>The transformation: what comes closer to the viewer is larger, and what reaches the viewer has no image.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="distance"/> is not a positive finite number.</exception>
    public static Transform3D CreatePerspective(double distance)
    {
        if (!(distance > 0) || !double.IsFinite(distance))
            throw new ArgumentOutOfRangeException(nameof(distance), distance, "The distance must be a positive finite number.");

        return new(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, -1 / distance, 0, 0, 0, 1);
    }

    /// <summary>Gets the transformation that applies this one first, then another one.</summary>
    /// <param name="next">The transformation applied after this one.</param>
    /// <returns>The combined transformation.</returns>
    /// <exception cref="ArgumentException">A coefficient of the result is not a finite number.</exception>
    public Transform3D Then(Transform3D next)
    {
        var result = new double[Count];
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                // The sum is evaluated in this order
                var value = (((Get(row * 4) * next.Get(column)) + (Get((row * 4) + 1) * next.Get(4 + column))) + (Get((row * 4) + 2) * next.Get(8 + column))) + (Get((row * 4) + 3) * next.Get(12 + column));
                if (!double.IsFinite(value))
                    throw new ArgumentException("A coefficient of the combined transformation is not a finite number.", nameof(next));

                result[(row * 4) + column] = value;
            }
        }

        return new Transform3D(result);
    }

    /// <summary>Gets the transformation that applies a first one, then a second one.</summary>
    /// <param name="first">The transformation applied first.</param>
    /// <param name="second">The transformation applied to the result of the first one.</param>
    /// <returns>The combined transformation.</returns>
    /// <exception cref="ArgumentException">A coefficient of the result is not a finite number.</exception>
    public static Transform3D Multiply(Transform3D first, Transform3D second) => first.Then(second);

    /// <summary>
    /// Gets what the transformation does to the plane <c>z = 0</c>, seen from the viewer: the point <c>(x, y)</c> becomes
    /// <c>((x M11 + y M21 + M41) / W, (x M12 + y M22 + M42) / W)</c> with <c>W = x M14 + y M24 + M44</c>.
    /// </summary>
    /// <returns>The projective transformation of the plane.</returns>
    public ProjectiveTransform ToProjectiveTransform() => new(M11, M12, M14, M21, M22, M24, M41, M42, M44);

    /// <inheritdoc />
    public bool Equals(Transform3D other)
    {
        for (var i = 0; i < Count; i++)
        {
            if (!Get(i).Equals(other.Get(i)))
                return false;
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Transform3D other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        for (var i = 0; i < Count; i++)
        {
            hash.Add(Get(i));
        }

        return hash.ToHashCode();
    }

    /// <summary>Returns the coefficients row by row, formatted as <c>[M11 M12 ... M44]</c>.</summary>
    /// <returns>A string representation of the transformation.</returns>
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"[{M11} {M12} {M13} {M14} {M21} {M22} {M23} {M24} {M31} {M32} {M33} {M34} {M41} {M42} {M43} {M44}]");

    /// <summary>Compares two transformations for equality.</summary>
    /// <param name="left">The first transformation.</param>
    /// <param name="right">The second transformation.</param>
    /// <returns><see langword="true"/> if every coefficient is equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Transform3D left, Transform3D right) => left.Equals(right);

    /// <summary>Compares two transformations for inequality.</summary>
    /// <param name="left">The first transformation.</param>
    /// <param name="right">The second transformation.</param>
    /// <returns><see langword="true"/> if a coefficient differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Transform3D left, Transform3D right) => !left.Equals(right);

    /// <summary>Gets the transformation that applies a first one, then a second one.</summary>
    /// <param name="left">The transformation applied first.</param>
    /// <param name="right">The transformation applied to the result of the first one.</param>
    /// <returns>The combined transformation.</returns>
    /// <exception cref="ArgumentException">A coefficient of the result is not a finite number.</exception>
    public static Transform3D operator *(Transform3D left, Transform3D right) => left.Then(right);

    private double Get(int index) => _values is null ? ((index % 5) == 0 ? 1 : 0) : _values[index];

    private static void GetSinCos(double degrees, string paramName, out double sin, out double cos)
    {
        if (!double.IsFinite(degrees))
            throw new ArgumentException("The value must be a finite number.", paramName);

        DeterministicAngle.SinCos(degrees / 180, out sin, out cos);

        // Adding zero turns a negative zero into zero: a quarter turn has no sign left in its coefficients
        sin += 0.0;
        cos += 0.0;
    }

    private static string GetName(int index) => index switch
    {
        0 => "m11",
        1 => "m12",
        2 => "m13",
        3 => "m14",
        4 => "m21",
        5 => "m22",
        6 => "m23",
        7 => "m24",
        8 => "m31",
        9 => "m32",
        10 => "m33",
        11 => "m34",
        12 => "m41",
        13 => "m42",
        14 => "m43",
        _ => "m44",
    };
}
