namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Expands one component to three by scaling it (a gray value to a neutral color of the connection space).</summary>
internal sealed class IccGrayExpandStage(double scale0, double scale1, double scale2) : IccStage
{
    // A linear function: it has no range
    public override bool SupportsUnbounded => true;

    public override void ApplyUnbounded(Span<double> values) => Apply(values);

    public override void Apply(Span<double> values)
    {
        var value = values[0];
        values[0] = value * scale0;
        values[1] = value * scale1;
        values[2] = value * scale2;
    }

    public override void ApplyBatch(Span<double> components, int count)
    {
        var first = GetComponent(components, 0, count);
        var second = GetComponent(components, 1, count);
        var third = GetComponent(components, 2, count);
        for (var i = 0; i < first.Length; i++)
        {
            var value = first[i];
            first[i] = value * scale0;
            second[i] = value * scale1;
            third[i] = value * scale2;
        }
    }
}
