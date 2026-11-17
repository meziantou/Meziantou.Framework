using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>A corner found by <see cref="ImageProcessingExtensions.DetectCorners(ImageFrame, CornerDetectionOptions?, CancellationToken)"/>: a pixel and the strength of its response.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct ImageCorner : IEquatable<ImageCorner>
{
    /// <summary>Initializes a new instance of the <see cref="ImageCorner"/> struct.</summary>
    /// <param name="x">The column of the pixel.</param>
    /// <param name="y">The row of the pixel.</param>
    /// <param name="response">The response of the detector at the pixel.</param>
    public ImageCorner(int x, int y, double response)
    {
        X = x;
        Y = y;
        Response = response;
    }

    /// <summary>Gets the column of the pixel.</summary>
    public int X { get; }

    /// <summary>Gets the row of the pixel.</summary>
    public int Y { get; }

    /// <summary>Gets the pixel.</summary>
    public Point Location => new(X, Y);

    /// <summary>
    /// Gets the response of the detector at the pixel, larger for a stronger corner: the Harris measure
    /// <c>det(M) - k trace(M)²</c>, the smaller eigenvalue of <c>M</c> (Shi-Tomasi), with <c>M</c> the structure tensor of the
    /// samples as fractions of 1; or the FAST score, the larger sum of the differences beyond the threshold of the brighter or
    /// of the darker pixels of the circle, in fractions of 1.
    /// </summary>
    public double Response { get; }

    /// <inheritdoc />
    public bool Equals(ImageCorner other) => X == other.X && Y == other.Y && Response.Equals(other.Response);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ImageCorner other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y, Response);

    /// <summary>Returns the corner formatted as <c>({X}, {Y}) {Response}</c>.</summary>
    /// <returns>A string representation of the corner.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({X}, {Y}) {Response}");

    /// <summary>Compares two corners for equality.</summary>
    /// <param name="left">The first corner.</param>
    /// <param name="right">The second corner.</param>
    /// <returns><see langword="true"/> if both corners are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(ImageCorner left, ImageCorner right) => left.Equals(right);

    /// <summary>Compares two corners for inequality.</summary>
    /// <param name="left">The first corner.</param>
    /// <param name="right">The second corner.</param>
    /// <returns><see langword="true"/> if the corners differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(ImageCorner left, ImageCorner right) => !left.Equals(right);
}
