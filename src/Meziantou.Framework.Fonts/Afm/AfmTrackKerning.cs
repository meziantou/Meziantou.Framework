using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// A track of an AFM file (the <c>TrackKern</c> key): a degree of tightness, and the change of the space between
/// characters as a linear function of the point size (Adobe Technical Note #5004, section 9.1).
/// </summary>
/// <remarks>Unlike the other metrics of the file, the kerning amounts of a track are in points.</remarks>
/// <param name="Degree">The degree of tightness: negative values are tighter, positive values looser.</param>
/// <param name="MinPointSize">The point size below which the kerning is <paramref name="MinKern"/>.</param>
/// <param name="MinKern">The kerning, in points, at <paramref name="MinPointSize"/>.</param>
/// <param name="MaxPointSize">The point size above which the kerning is <paramref name="MaxKern"/>.</param>
/// <param name="MaxKern">The kerning, in points, at <paramref name="MaxPointSize"/>.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct AfmTrackKerning(int Degree, double MinPointSize, double MinKern, double MaxPointSize, double MaxKern)
{
    /// <summary>Gets the kerning of the track at a point size.</summary>
    /// <param name="pointSize">The point size of the font.</param>
    /// <returns>The amount, in points, to add to the space between characters: negative values bring them closer.</returns>
    public double GetKerning(double pointSize)
    {
        // k(p) = k0 for p < p0, k1 for p > p1, and the line between (p0, k0) and (p1, k1) in between.
        if (pointSize <= MinPointSize)
            return MinKern;

        if (pointSize >= MaxPointSize)
            return MaxKern;

        // The fraction of the way from p0 to p1, between 0 and 1, keeps the result between k0 and k1 even when the
        // point sizes are very close: the slope (k1 - k0) / (p1 - p0) of the specification can overflow.
        var fraction = (pointSize - MinPointSize) / (MaxPointSize - MinPointSize);
        return MinKern + ((MaxKern - MinKern) * fraction);
    }
}
