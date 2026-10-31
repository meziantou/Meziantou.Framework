namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Reduces three components to one scaled component (the lightness of a color of the connection space).</summary>
internal sealed class IccGrayReduceStage(int index, double scale) : IccStage
{
    // A linear function: it has no range
    public override bool SupportsUnbounded => true;

    public override void ApplyUnbounded(Span<double> values) => Apply(values);

    public override void Apply(Span<double> values) => values[0] = values[index] * scale;

    public override void ApplyBatch(Span<double> components, int count)
    {
        var result = GetComponent(components, 0, count);
        var source = GetComponent(components, index, count);
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = source[i] * scale;
        }
    }
}
