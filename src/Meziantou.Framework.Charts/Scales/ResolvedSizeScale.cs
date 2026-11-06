namespace Meziantou.Framework.Charts;

/// <summary>A size scale with its domain: the radius is proportional to the square root of the value.</summary>
internal sealed class ResolvedSizeScale(double maximum, double maxRadius)
{
    public double Map(double value)
    {
        if (double.IsNaN(value) || value <= 0 || maximum <= 0)
            return 0;

        return maxRadius * Math.Sqrt(value / maximum);
    }
}
