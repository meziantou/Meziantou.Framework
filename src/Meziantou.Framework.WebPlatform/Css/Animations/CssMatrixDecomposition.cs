namespace Meziantou.Framework.WebPlatform.Css.Animations;

/// <summary>
/// The decomposition and recomposition of the transformation matrices, used to interpolate them
/// (https://drafts.csswg.org/css-transforms-2/#interpolation-of-3d-matrices, https://drafts.csswg.org/css-transforms-1/#interpolation-of-2d-matrices).
/// </summary>
/// <remarks>
/// The matrices are 4x4 column-major arrays (see <see cref="Values.CssTransforms"/>): matrix[i][j] of the specification is
/// m[(i * 4) + j], and matrix[3][0..2] is the translation.
/// </remarks>
internal static class CssMatrixDecomposition
{
    /// <summary>
    /// A decomposed 2D matrix: translate × rotate(<paramref name="Angle"/>) × skew(<paramref name="Skew"/>) × scale, the angle
    /// in degrees and the skew the tangent of the angle of the XY shear.
    /// </summary>
    public sealed record Decomposed2D(double TranslateX, double TranslateY, double ScaleX, double ScaleY, double Skew, double Angle);

    /// <summary>A decomposed 3D matrix (https://drafts.csswg.org/css-transforms-2/#decomposing-a-3d-matrix).</summary>
    public sealed record Decomposed3D(double[] Translate, double[] Scale, double[] Skew, double[] Perspective, double[] Quaternion);

    /// <summary>Returns whether a matrix is a 2D matrix (https://drafts.csswg.org/css-transforms-2/#2d-matrix).</summary>
    public static bool Is2D(ReadOnlySpan<double> m) =>
        m[2] == 0 && m[3] == 0 && m[6] == 0 && m[7] == 0 && m[8] == 0 && m[9] == 0 && m[10] == 1 && m[11] == 0 && m[14] == 0 && m[15] == 1;

    /// <summary>
    /// Decomposes a 2D matrix into a translation, a rotation, a skew and a scale; returns null when the matrix is singular.
    /// </summary>
    /// <remarks>
    /// The algorithm of https://drafts.csswg.org/css-transforms-1/#decomposing-a-2d-matrix does not round-trip the matrices
    /// that have a skew (the matrix that remains after the rotation is interpolated as is). Like Blink and Gecko, the 2D
    /// matrices are decomposed like the 3D ones (https://drafts.csswg.org/css-transforms-2/#decomposing-a-3d-matrix), with the
    /// rule of the 2D algorithm for the matrices with a negative determinant: the axis with the minimum unit vector dot
    /// product is flipped.
    /// </remarks>
    public static Decomposed2D? Decompose2D(ReadOnlySpan<double> m)
    {
        var m11 = m[0];
        var m12 = m[1];
        var m21 = m[4];
        var m22 = m[5];
        var determinant = (m11 * m22) - (m12 * m21);
        if (determinant == 0)
            return null;

        double scaleX = 1;
        double scaleY = 1;
        if (determinant < 0)
        {
            if (m11 < m22)
            {
                scaleX = -1;
            }
            else
            {
                scaleY = -1;
            }
        }

        // The x scale, then the row is normalized
        scaleX *= Math.Sqrt((m11 * m11) + (m12 * m12));
        m11 /= scaleX;
        m12 /= scaleX;

        // The XY shear: the second row is made orthogonal to the first one
        var shear = (m11 * m21) + (m12 * m22);
        m21 -= m11 * shear;
        m22 -= m12 * shear;

        // The y scale
        scaleY *= Math.Sqrt((m21 * m21) + (m22 * m22));
        return new Decomposed2D(m[12], m[13], scaleX, scaleY, shear / scaleY, Math.Atan2(m12, m11) * 180 / Math.PI);
    }

    /// <summary>
    /// Interpolates two decomposed 2D matrices (https://drafts.csswg.org/css-transforms-1/#interpolation-of-decomposed-2d-matrix-values):
    /// the translations, the scales and the skews are interpolated linearly, and the rotation does not go the long way around.
    /// </summary>
    public static Decomposed2D Interpolate(Decomposed2D a, Decomposed2D b, double progress)
    {
        var scaleAX = a.ScaleX;
        var scaleAY = a.ScaleY;
        var angleA = a.Angle;
        var angleB = b.Angle;

        // If the x axis of one is flipped and the y axis of the other is flipped, the flip is converted to a rotation
        if ((scaleAX < 0 && b.ScaleY < 0) || (scaleAY < 0 && b.ScaleX < 0))
        {
            scaleAX = -scaleAX;
            scaleAY = -scaleAY;
            angleA += angleA < 0 ? 180 : -180;
        }

        // The rotation does not go the long way around
        if (angleA == 0)
        {
            angleA = 360;
        }

        if (angleB == 0)
        {
            angleB = 360;
        }

        if (Math.Abs(angleA - angleB) > 180)
        {
            if (angleA > angleB)
            {
                angleA -= 360;
            }
            else
            {
                angleB -= 360;
            }
        }

        return new Decomposed2D(
            Lerp(a.TranslateX, b.TranslateX, progress),
            Lerp(a.TranslateY, b.TranslateY, progress),
            Lerp(scaleAX, b.ScaleX, progress),
            Lerp(scaleAY, b.ScaleY, progress),
            Lerp(a.Skew, b.Skew, progress),
            Lerp(angleA, angleB, progress));
    }

    /// <summary>Recomposes a 2D matrix: translate × rotate × skew × scale.</summary>
    public static double[] Recompose(Decomposed2D d)
    {
        var angle = d.Angle * Math.PI / 180;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var a = cos * d.ScaleX;
        var b = sin * d.ScaleX;
        var c = ((cos * d.Skew) - sin) * d.ScaleY;
        var e = ((sin * d.Skew) + cos) * d.ScaleY;
        return [a, b, 0, 0, c, e, 0, 0, 0, 0, 1, 0, d.TranslateX, d.TranslateY, 0, 1];
    }

    /// <summary>https://drafts.csswg.org/css-transforms-2/#decomposing-a-3d-matrix; returns null when the matrix cannot be decomposed.</summary>
    public static Decomposed3D? Decompose3D(ReadOnlySpan<double> source)
    {
        if (source[15] == 0)
            return null;

        // Normalize the matrix
        var m = new double[16];
        for (var i = 0; i < 16; i++)
        {
            m[i] = source[i] / source[15];
        }

        // The perspective matrix is used to solve for the perspective, and to test the singularity of the upper 3x3 component
        var perspectiveMatrix = (double[])m.Clone();
        perspectiveMatrix[3] = 0;
        perspectiveMatrix[7] = 0;
        perspectiveMatrix[11] = 0;
        perspectiveMatrix[15] = 1;
        if (Determinant(perspectiveMatrix) == 0)
            return null;

        double[] perspective;
        if (m[3] != 0 || m[7] != 0 || m[11] != 0)
        {
            double[] rightHandSide = [m[3], m[7], m[11], m[15]];
            var inverse = Inverse(perspectiveMatrix);
            if (inverse is null)
                return null;

            // perspective = rightHandSide × transpose(inverse)
            perspective = new double[4];
            for (var i = 0; i < 4; i++)
            {
                double sum = 0;
                for (var j = 0; j < 4; j++)
                {
                    sum += rightHandSide[j] * inverse[(i * 4) + j];
                }

                perspective[i] = sum;
            }
        }
        else
        {
            perspective = [0, 0, 0, 1];
        }

        double[] translate = [m[12], m[13], m[14]];
        var row = new double[3][];
        for (var i = 0; i < 3; i++)
        {
            row[i] = [m[i * 4], m[(i * 4) + 1], m[(i * 4) + 2]];
        }

        var scale = new double[3];
        var skew = new double[3];

        // The X scale factor, then the XY shear (the second row is made orthogonal to the first)
        scale[0] = Length(row[0]);
        Normalize(row[0]);
        skew[0] = Dot(row[0], row[1]);
        row[1] = Combine(row[1], row[0], 1, -skew[0]);

        // The Y scale
        scale[1] = Length(row[1]);
        Normalize(row[1]);
        skew[0] /= scale[1];

        // The XZ and YZ shears (the third row is made orthogonal)
        skew[1] = Dot(row[0], row[2]);
        row[2] = Combine(row[2], row[0], 1, -skew[1]);
        skew[2] = Dot(row[1], row[2]);
        row[2] = Combine(row[2], row[1], 1, -skew[2]);

        // The Z scale
        scale[2] = Length(row[2]);
        Normalize(row[2]);
        skew[1] /= scale[2];
        skew[2] /= scale[2];

        // The matrix is orthonormal: when the determinant is -1 (a coordinate system flip), the matrix and the scales are negated
        if (Dot(row[0], Cross(row[1], row[2])) < 0)
        {
            for (var i = 0; i < 3; i++)
            {
                scale[i] = -scale[i];
                row[i][0] = -row[i][0];
                row[i][1] = -row[i][1];
                row[i][2] = -row[i][2];
            }
        }

        // The rotation
        var quaternion = new double[4];
        quaternion[0] = 0.5 * Math.Sqrt(Math.Max(1 + row[0][0] - row[1][1] - row[2][2], 0));
        quaternion[1] = 0.5 * Math.Sqrt(Math.Max(1 - row[0][0] + row[1][1] - row[2][2], 0));
        quaternion[2] = 0.5 * Math.Sqrt(Math.Max(1 - row[0][0] - row[1][1] + row[2][2], 0));
        quaternion[3] = 0.5 * Math.Sqrt(Math.Max(1 + row[0][0] + row[1][1] + row[2][2], 0));
        if (row[2][1] > row[1][2])
        {
            quaternion[0] = -quaternion[0];
        }

        if (row[0][2] > row[2][0])
        {
            quaternion[1] = -quaternion[1];
        }

        if (row[1][0] > row[0][1])
        {
            quaternion[2] = -quaternion[2];
        }

        return new Decomposed3D(translate, scale, skew, perspective, quaternion);
    }

    /// <summary>https://drafts.csswg.org/css-transforms-2/#interpolation-of-decomposed-3d-matrix-values</summary>
    public static Decomposed3D Interpolate(Decomposed3D a, Decomposed3D b, double progress)
    {
        return new Decomposed3D(
            LerpVector(a.Translate, b.Translate, progress),
            LerpVector(a.Scale, b.Scale, progress),
            LerpVector(a.Skew, b.Skew, progress),
            LerpVector(a.Perspective, b.Perspective, progress),
            Slerp(a.Quaternion, b.Quaternion, progress));
    }

    /// <summary>
    /// Accumulates two decomposed matrices (https://drafts.csswg.org/css-transforms-2/#accumulate-transforms): the translations,
    /// skews and perspectives are added, the scales are combined (a + b - 1), and the rotations are composed.
    /// </summary>
    public static Decomposed3D Accumulate(Decomposed3D a, Decomposed3D b)
    {
        var scale = new double[3];
        for (var i = 0; i < 3; i++)
        {
            scale[i] = a.Scale[i] + b.Scale[i] - 1;
        }

        var perspective = new double[4];
        for (var i = 0; i < 4; i++)
        {
            perspective[i] = a.Perspective[i] + b.Perspective[i] - (i == 3 ? 1 : 0);
        }

        return new Decomposed3D(AddVectors(a.Translate, b.Translate), scale, AddVectors(a.Skew, b.Skew), perspective, MultiplyQuaternions(a.Quaternion, b.Quaternion));
    }

    /// <summary>https://drafts.csswg.org/css-transforms-2/#recomposing-to-a-3d-matrix</summary>
    public static double[] Recompose(Decomposed3D d)
    {
        double[] m = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

        // The perspective
        for (var i = 0; i < 4; i++)
        {
            m[(i * 4) + 3] = d.Perspective[i];
        }

        // The translation
        for (var i = 0; i < 4; i++)
        {
            for (var j = 0; j < 3; j++)
            {
                m[12 + i] += d.Translate[j] * m[(j * 4) + i];
            }
        }

        // The rotation
        var x = d.Quaternion[0];
        var y = d.Quaternion[1];
        var z = d.Quaternion[2];
        var w = d.Quaternion[3];
        // The rotation matrix of the quaternion (the matrix of the specification is written in rows; the arrays are columns)
        double[] rotation =
        [
            1 - (2 * ((y * y) + (z * z))), 2 * ((x * y) + (z * w)), 2 * ((x * z) - (y * w)), 0,
            2 * ((x * y) - (z * w)), 1 - (2 * ((x * x) + (z * z))), 2 * ((y * z) + (x * w)), 0,
            2 * ((x * z) + (y * w)), 2 * ((y * z) - (x * w)), 1 - (2 * ((x * x) + (y * y))), 0,
            0, 0, 0, 1,
        ];
        m = MultiplySpec(m, rotation);

        // The skews
        double[] temp = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        if (d.Skew[2] != 0)
        {
            temp[9] = d.Skew[2];
            m = MultiplySpec(m, temp);
        }

        if (d.Skew[1] != 0)
        {
            temp[9] = 0;
            temp[8] = d.Skew[1];
            m = MultiplySpec(m, temp);
        }

        if (d.Skew[0] != 0)
        {
            temp[8] = 0;
            temp[4] = d.Skew[0];
            m = MultiplySpec(m, temp);
        }

        // The scale
        for (var i = 0; i < 3; i++)
        {
            for (var j = 0; j < 4; j++)
            {
                m[(i * 4) + j] *= d.Scale[i];
            }
        }

        return m;
    }

    /// <summary>The multiplication of the specification: multiply(a, b) is the transformation b followed by a, i.e. b × a in the column-major layout.</summary>
    private static double[] MultiplySpec(double[] a, double[] b) => Values.CssTransforms.Multiply(a, b);

    /// <summary>Spherical linear interpolation of two quaternions (https://drafts.csswg.org/css-transforms-2/#interpolation-of-decomposed-3d-matrix-values).</summary>
    public static double[] Slerp(double[] a, double[] b, double progress)
    {
        var product = Math.Clamp((a[0] * b[0]) + (a[1] * b[1]) + (a[2] * b[2]) + (a[3] * b[3]), -1, 1);
        if (Math.Abs(product) >= 1 - 1e-10)
            return (double[])a.Clone();

        var theta = Math.Acos(product);
        var w = Math.Sin(progress * theta) / Math.Sqrt(1 - (product * product));
        var scaleA = Math.Cos(progress * theta) - (product * w);
        var result = new double[4];
        for (var i = 0; i < 4; i++)
        {
            result[i] = (a[i] * scaleA) + (b[i] * w);
        }

        return result;
    }

    /// <summary>The product of two quaternions (x, y, z, w).</summary>
    public static double[] MultiplyQuaternions(double[] a, double[] b) =>
    [
        (a[3] * b[0]) + (a[0] * b[3]) + (a[1] * b[2]) - (a[2] * b[1]),
        (a[3] * b[1]) - (a[0] * b[2]) + (a[1] * b[3]) + (a[2] * b[0]),
        (a[3] * b[2]) + (a[0] * b[1]) - (a[1] * b[0]) + (a[2] * b[3]),
        (a[3] * b[3]) - (a[0] * b[0]) - (a[1] * b[1]) - (a[2] * b[2]),
    ];

    private static double Lerp(double a, double b, double progress) => (a * (1 - progress)) + (b * progress);

    private static double[] LerpVector(double[] a, double[] b, double progress)
    {
        var result = new double[a.Length];
        for (var i = 0; i < a.Length; i++)
        {
            result[i] = Lerp(a[i], b[i], progress);
        }

        return result;
    }

    private static double[] AddVectors(double[] a, double[] b)
    {
        var result = new double[a.Length];
        for (var i = 0; i < a.Length; i++)
        {
            result[i] = a[i] + b[i];
        }

        return result;
    }

    private static double Length(double[] v) => Math.Sqrt((v[0] * v[0]) + (v[1] * v[1]) + (v[2] * v[2]));

    private static void Normalize(double[] v)
    {
        var length = Length(v);
        if (length == 0)
            return;

        v[0] /= length;
        v[1] /= length;
        v[2] /= length;
    }

    private static double Dot(double[] a, double[] b) => (a[0] * b[0]) + (a[1] * b[1]) + (a[2] * b[2]);

    private static double[] Cross(double[] a, double[] b) => [(a[1] * b[2]) - (a[2] * b[1]), (a[2] * b[0]) - (a[0] * b[2]), (a[0] * b[1]) - (a[1] * b[0])];

    private static double[] Combine(double[] a, double[] b, double scaleA, double scaleB) => [(a[0] * scaleA) + (b[0] * scaleB), (a[1] * scaleA) + (b[1] * scaleB), (a[2] * scaleA) + (b[2] * scaleB)];

    /// <summary>The determinant of a 4x4 matrix.</summary>
    public static double Determinant(ReadOnlySpan<double> m)
    {
        var inverse = Cofactors(m, out var determinant);
        _ = inverse;
        return determinant;
    }

    /// <summary>The inverse of a 4x4 matrix, or null when it is singular.</summary>
    public static double[]? Inverse(ReadOnlySpan<double> m)
    {
        var cofactors = Cofactors(m, out var determinant);
        if (determinant == 0)
            return null;

        for (var i = 0; i < 16; i++)
        {
            cofactors[i] /= determinant;
        }

        return cofactors;
    }

    /// <summary>Returns the adjugate of a 4x4 matrix (the inverse multiplied by the determinant) and its determinant.</summary>
    private static double[] Cofactors(ReadOnlySpan<double> m, out double determinant)
    {
        var inv = new double[16];
        inv[0] = (m[5] * m[10] * m[15]) - (m[5] * m[11] * m[14]) - (m[9] * m[6] * m[15]) + (m[9] * m[7] * m[14]) + (m[13] * m[6] * m[11]) - (m[13] * m[7] * m[10]);
        inv[4] = (-m[4] * m[10] * m[15]) + (m[4] * m[11] * m[14]) + (m[8] * m[6] * m[15]) - (m[8] * m[7] * m[14]) - (m[12] * m[6] * m[11]) + (m[12] * m[7] * m[10]);
        inv[8] = (m[4] * m[9] * m[15]) - (m[4] * m[11] * m[13]) - (m[8] * m[5] * m[15]) + (m[8] * m[7] * m[13]) + (m[12] * m[5] * m[11]) - (m[12] * m[7] * m[9]);
        inv[12] = (-m[4] * m[9] * m[14]) + (m[4] * m[10] * m[13]) + (m[8] * m[5] * m[14]) - (m[8] * m[6] * m[13]) - (m[12] * m[5] * m[10]) + (m[12] * m[6] * m[9]);
        inv[1] = (-m[1] * m[10] * m[15]) + (m[1] * m[11] * m[14]) + (m[9] * m[2] * m[15]) - (m[9] * m[3] * m[14]) - (m[13] * m[2] * m[11]) + (m[13] * m[3] * m[10]);
        inv[5] = (m[0] * m[10] * m[15]) - (m[0] * m[11] * m[14]) - (m[8] * m[2] * m[15]) + (m[8] * m[3] * m[14]) + (m[12] * m[2] * m[11]) - (m[12] * m[3] * m[10]);
        inv[9] = (-m[0] * m[9] * m[15]) + (m[0] * m[11] * m[13]) + (m[8] * m[1] * m[15]) - (m[8] * m[3] * m[13]) - (m[12] * m[1] * m[11]) + (m[12] * m[3] * m[9]);
        inv[13] = (m[0] * m[9] * m[14]) - (m[0] * m[10] * m[13]) - (m[8] * m[1] * m[14]) + (m[8] * m[2] * m[13]) + (m[12] * m[1] * m[10]) - (m[12] * m[2] * m[9]);
        inv[2] = (m[1] * m[6] * m[15]) - (m[1] * m[7] * m[14]) - (m[5] * m[2] * m[15]) + (m[5] * m[3] * m[14]) + (m[13] * m[2] * m[7]) - (m[13] * m[3] * m[6]);
        inv[6] = (-m[0] * m[6] * m[15]) + (m[0] * m[7] * m[14]) + (m[4] * m[2] * m[15]) - (m[4] * m[3] * m[14]) - (m[12] * m[2] * m[7]) + (m[12] * m[3] * m[6]);
        inv[10] = (m[0] * m[5] * m[15]) - (m[0] * m[7] * m[13]) - (m[4] * m[1] * m[15]) + (m[4] * m[3] * m[13]) + (m[12] * m[1] * m[7]) - (m[12] * m[3] * m[5]);
        inv[14] = (-m[0] * m[5] * m[14]) + (m[0] * m[6] * m[13]) + (m[4] * m[1] * m[14]) - (m[4] * m[2] * m[13]) - (m[12] * m[1] * m[6]) + (m[12] * m[2] * m[5]);
        inv[3] = (-m[1] * m[6] * m[11]) + (m[1] * m[7] * m[10]) + (m[5] * m[2] * m[11]) - (m[5] * m[3] * m[10]) - (m[9] * m[2] * m[7]) + (m[9] * m[3] * m[6]);
        inv[7] = (m[0] * m[6] * m[11]) - (m[0] * m[7] * m[10]) - (m[4] * m[2] * m[11]) + (m[4] * m[3] * m[10]) + (m[8] * m[2] * m[7]) - (m[8] * m[3] * m[6]);
        inv[11] = (-m[0] * m[5] * m[11]) + (m[0] * m[7] * m[9]) + (m[4] * m[1] * m[11]) - (m[4] * m[3] * m[9]) - (m[8] * m[1] * m[7]) + (m[8] * m[3] * m[5]);
        inv[15] = (m[0] * m[5] * m[10]) - (m[0] * m[6] * m[9]) - (m[4] * m[1] * m[10]) + (m[4] * m[2] * m[9]) + (m[8] * m[1] * m[6]) - (m[8] * m[2] * m[5]);
        determinant = (m[0] * inv[0]) + (m[1] * inv[4]) + (m[2] * inv[8]) + (m[3] * inv[12]);
        return inv;
    }
}
