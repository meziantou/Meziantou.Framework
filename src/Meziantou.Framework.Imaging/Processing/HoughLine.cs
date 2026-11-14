using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// A straight line found by <see cref="ImageProcessingExtensions.DetectLines(ImageFrame, HoughLineOptions?, CancellationToken)"/>,
/// in normal form: the points <c>(x, y)</c> with <c>x cos θ + y sin θ = ρ</c>.
/// </summary>
/// <remarks>
/// The coordinates are those of the stored pixels, where the frame covers <c>[0, width] x [0, height]</c> and the center
/// of pixel <c>(i, j)</c> is <c>(i + 1/2, j + 1/2)</c>; y grows downward. An angle of 0 is a vertical line
/// (<c>x = ρ</c>), an angle of 90 a horizontal one (<c>y = ρ</c>).
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct HoughLine : IEquatable<HoughLine>
{
    /// <summary>Initializes a new instance of the <see cref="HoughLine"/> struct.</summary>
    /// <param name="angle">The angle θ of the normal of the line, in degrees.</param>
    /// <param name="distance">The signed distance ρ of the line to the origin, in pixels.</param>
    /// <param name="votes">The number of edge pixels on the line.</param>
    public HoughLine(double angle, double distance, int votes)
    {
        Angle = angle;
        Distance = distance;
        Votes = votes;
    }

    /// <summary>Gets the angle θ of the normal of the line, in degrees, from 0 (included) to 180 (excluded), clockwise from the x axis since y grows downward.</summary>
    public double Angle { get; }

    /// <summary>Gets the signed distance ρ of the line to the origin (the top-left corner of the frame), in pixels.</summary>
    public double Distance { get; }

    /// <summary>Gets the number of edge pixels that voted for the line: the length of the visible part of the line, roughly.</summary>
    public int Votes { get; }

    /// <summary>
    /// Gets the angle of the direction of the line, in degrees, from -90 (excluded) to 90 (included): 0 for a horizontal line,
    /// positive when it goes down to the right (clockwise, since y grows downward).
    /// </summary>
    public double Slope => Angle - 90 is var slope && slope <= -90 ? slope + 180 : slope;

    /// <inheritdoc />
    public bool Equals(HoughLine other) => Angle.Equals(other.Angle) && Distance.Equals(other.Distance) && Votes == other.Votes;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is HoughLine other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Angle, Distance, Votes);

    /// <summary>Returns the line formatted as <c>θ={Angle} ρ={Distance} ({Votes} votes)</c>.</summary>
    /// <returns>A string representation of the line.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"θ={Angle} ρ={Distance} ({Votes} votes)");

    /// <summary>Compares two lines for equality.</summary>
    /// <param name="left">The first line.</param>
    /// <param name="right">The second line.</param>
    /// <returns><see langword="true"/> if both lines are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(HoughLine left, HoughLine right) => left.Equals(right);

    /// <summary>Compares two lines for inequality.</summary>
    /// <param name="left">The first line.</param>
    /// <param name="right">The second line.</param>
    /// <returns><see langword="true"/> if the lines differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(HoughLine left, HoughLine right) => !left.Equals(right);
}
