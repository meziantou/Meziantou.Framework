using System.Numerics;

namespace Meziantou.Framework.Imaging.TestHarness.AutoCrop;

/// <summary>The exact result of a reference auto-crop analysis.</summary>
/// <param name="Success">Whether a border was found around a content box of at least 3x3 pixels.</param>
/// <param name="UsedRetry">Whether the first border pass failed, so that the retry on the inset rectangle decided.</param>
/// <param name="X">The left column of the content box (0 when the analysis failed).</param>
/// <param name="Y">The top row of the content box (0 when the analysis failed).</param>
/// <param name="Width">The width of the content box (the canvas width when the analysis failed).</param>
/// <param name="Height">The height of the content box (the canvas height when the analysis failed).</param>
/// <param name="Background">The red, green, blue and alpha samples of the background, at the precision of the analyzed buffers.</param>
/// <param name="WeightXNumerator">The numerator of the exact horizontal weight (0 when weights are not analyzed).</param>
/// <param name="WeightYNumerator">The numerator of the exact vertical weight.</param>
/// <param name="WeightXDenominator">The positive denominator of the exact horizontal weight.</param>
/// <param name="WeightYDenominator">The positive denominator of the exact vertical weight.</param>
public sealed record ReferenceAutoCropAnalysis(
    bool Success,
    bool UsedRetry,
    int X,
    int Y,
    int Width,
    int Height,
    (int Red, int Green, int Blue, int Alpha) Background,
    BigInteger WeightXNumerator,
    BigInteger WeightYNumerator,
    BigInteger WeightXDenominator,
    BigInteger WeightYDenominator)
{
    /// <summary>Gets the horizontal weight, rounded to a double.</summary>
    public double WeightX => (double)WeightXNumerator / (double)WeightXDenominator;

    /// <summary>Gets the vertical weight, rounded to a double.</summary>
    public double WeightY => (double)WeightYNumerator / (double)WeightYDenominator;
}
