namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Applies one curve per component.</summary>
internal sealed class IccCurvesStage(IccCurve[] curves) : IccStage
{
    /// <summary>Gets the curves, one per component.</summary>
    public IccCurve[] Curves => curves;

    public override bool SupportsUnbounded => true;

    public override void ApplyUnbounded(Span<double> values)
    {
        for (var i = 0; i < curves.Length; i++)
        {
            values[i] = curves[i].EvaluateUnbounded(values[i]);
        }
    }

    public override void Apply(Span<double> values)
    {
        for (var i = 0; i < curves.Length; i++)
        {
            values[i] = curves[i].Evaluate(values[i]);
        }
    }

    public override void ApplyBatch(Span<double> components, int count)
    {
        for (var channel = 0; channel < curves.Length; channel++)
        {
            var curve = curves[channel];
            foreach (ref var value in GetComponent(components, channel, count))
            {
                value = curve.Evaluate(value);
            }
        }
    }
}
