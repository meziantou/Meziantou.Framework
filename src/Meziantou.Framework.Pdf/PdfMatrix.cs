using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Pdf;

/// <summary>A transformation matrix <c>[a b c d e f]</c> (ISO 32000-2, 8.3.4): a point (x, y) becomes (a\*x + c\*y + e, b\*x + d\*y + f).</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct PdfMatrix : IEquatable<PdfMatrix>
{
    /// <summary>Initializes a new instance of the <see cref="PdfMatrix"/> struct.</summary>
    /// <param name="a">The a value.</param>
    /// <param name="b">The b value.</param>
    /// <param name="c">The c value.</param>
    /// <param name="d">The d value.</param>
    /// <param name="e">The e value (horizontal translation).</param>
    /// <param name="f">The f value (vertical translation).</param>
    public PdfMatrix(double a, double b, double c, double d, double e, double f)
    {
        A = a;
        B = b;
        C = c;
        D = d;
        E = e;
        F = f;
    }

    /// <summary>Gets the identity matrix.</summary>
    public static PdfMatrix Identity { get; } = new(1, 0, 0, 1, 0, 0);

    /// <summary>Gets the a value.</summary>
    public double A { get; }

    /// <summary>Gets the b value.</summary>
    public double B { get; }

    /// <summary>Gets the c value.</summary>
    public double C { get; }

    /// <summary>Gets the d value.</summary>
    public double D { get; }

    /// <summary>Gets the e value (horizontal translation).</summary>
    public double E { get; }

    /// <summary>Gets the f value (vertical translation).</summary>
    public double F { get; }

    /// <summary>Multiplies two matrices: the result applies <paramref name="left"/> first, then <paramref name="right"/>.</summary>
    /// <param name="left">The first transformation.</param>
    /// <param name="right">The second transformation.</param>
    /// <returns>The product.</returns>
    public static PdfMatrix operator *(PdfMatrix left, PdfMatrix right) => Multiply(left, right);

    /// <summary>Indicates whether two matrices are equal.</summary>
    /// <param name="left">The first matrix.</param>
    /// <param name="right">The second matrix.</param>
    /// <returns><see langword="true"/> when the matrices are equal.</returns>
    public static bool operator ==(PdfMatrix left, PdfMatrix right) => left.Equals(right);

    /// <summary>Indicates whether two matrices are different.</summary>
    /// <param name="left">The first matrix.</param>
    /// <param name="right">The second matrix.</param>
    /// <returns><see langword="true"/> when the matrices are different.</returns>
    public static bool operator !=(PdfMatrix left, PdfMatrix right) => !left.Equals(right);

    /// <summary>Multiplies two matrices: the result applies <paramref name="left"/> first, then <paramref name="right"/>.</summary>
    /// <param name="left">The first transformation.</param>
    /// <param name="right">The second transformation.</param>
    /// <returns>The product.</returns>
    public static PdfMatrix Multiply(PdfMatrix left, PdfMatrix right)
    {
        return new PdfMatrix(
            (left.A * right.A) + (left.B * right.C),
            (left.A * right.B) + (left.B * right.D),
            (left.C * right.A) + (left.D * right.C),
            (left.C * right.B) + (left.D * right.D),
            (left.E * right.A) + (left.F * right.C) + right.E,
            (left.E * right.B) + (left.F * right.D) + right.F);
    }

    /// <summary>Creates a translation.</summary>
    /// <param name="x">The horizontal translation.</param>
    /// <param name="y">The vertical translation.</param>
    /// <returns>The matrix.</returns>
    public static PdfMatrix CreateTranslation(double x, double y) => new(1, 0, 0, 1, x, y);

    /// <summary>Creates a scaling.</summary>
    /// <param name="x">The horizontal scale.</param>
    /// <param name="y">The vertical scale.</param>
    /// <returns>The matrix.</returns>
    public static PdfMatrix CreateScale(double x, double y) => new(x, 0, 0, y, 0, 0);

    /// <summary>Creates a counterclockwise rotation.</summary>
    /// <param name="degrees">The angle in degrees.</param>
    /// <returns>The matrix.</returns>
    public static PdfMatrix CreateRotation(double degrees)
    {
        var radians = degrees * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        // Exact values for the right angles, so that rotated pages keep whole coordinates
        if (degrees % 90 == 0)
        {
            cos = Math.Round(cos, MidpointRounding.AwayFromZero);
            sin = Math.Round(sin, MidpointRounding.AwayFromZero);
        }

        return new PdfMatrix(cos, sin, -sin, cos, 0, 0);
    }

    /// <summary>Transforms a point.</summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <returns>The transformed point.</returns>
    public (double X, double Y) Transform(double x, double y) => ((A * x) + (C * y) + E, (B * x) + (D * y) + F);

    /// <summary>Transforms a vector: the translation is not applied.</summary>
    /// <param name="x">The x component.</param>
    /// <param name="y">The y component.</param>
    /// <returns>The transformed vector.</returns>
    public (double X, double Y) TransformVector(double x, double y) => ((A * x) + (C * y), (B * x) + (D * y));

    /// <summary>Inverts the matrix.</summary>
    /// <param name="result">The inverse.</param>
    /// <returns><see langword="true"/> when the matrix can be inverted.</returns>
    public bool TryInvert(out PdfMatrix result)
    {
        var determinant = (A * D) - (B * C);
        if (determinant == 0 || !double.IsFinite(determinant))
        {
            result = default;
            return false;
        }

        result = new PdfMatrix(D / determinant, -B / determinant, -C / determinant, A / determinant, ((C * F) - (D * E)) / determinant, ((B * E) - (A * F)) / determinant);
        return true;
    }

    /// <inheritdoc/>
    public bool Equals(PdfMatrix other) => A.Equals(other.A) && B.Equals(other.B) && C.Equals(other.C) && D.Equals(other.D) && E.Equals(other.E) && F.Equals(other.F);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is PdfMatrix other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(A, B, C, D, E, F);

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"[{A} {B} {C} {D} {E} {F}]");
}
