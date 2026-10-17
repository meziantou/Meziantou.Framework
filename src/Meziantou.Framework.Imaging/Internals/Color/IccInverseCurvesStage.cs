namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Applies one inverse curve per component.</summary>
internal sealed class IccInverseCurvesStage(IccInverseCurve[] curves) : IccStage
{
    /// <summary>Gets the curves, one per component.</summary>
    public IccInverseCurve[] Curves => curves;

    public override void Apply(Span<double> values)
    {
        for (var i = 0; i < curves.Length; i++)
        {
            values[i] = curves[i].Evaluate(values[i]);
        }
    }
}
