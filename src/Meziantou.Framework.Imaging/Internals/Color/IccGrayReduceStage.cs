namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Reduces three components to one scaled component (the lightness of a color of the connection space).</summary>
internal sealed class IccGrayReduceStage(int index, double scale) : IccStage
{
    public override void Apply(Span<double> values) => values[0] = values[index] * scale;
}
