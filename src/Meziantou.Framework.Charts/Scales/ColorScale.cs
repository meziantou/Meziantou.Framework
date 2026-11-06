namespace Meziantou.Framework.Charts;

/// <summary>Maps the values of the <c>Fill</c> and <c>Stroke</c> channels to colors. When a plot has no color scale, it uses an <see cref="OrdinalColorScale"/> for categories and a <see cref="SequentialColorScale"/> for numbers.</summary>
public abstract class ColorScale
{
    private protected ColorScale()
    {
    }
}
