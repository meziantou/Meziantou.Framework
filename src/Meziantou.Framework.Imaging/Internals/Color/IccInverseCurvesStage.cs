namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Applies one inverse curve per component.</summary>
internal sealed class IccInverseCurvesStage(IccInverseCurve[] curves) : IccStage
{
    public override void Apply(Span<double> values)
    {
        for (var i = 0; i < curves.Length; i++)
        {
            values[i] = curves[i].Evaluate(values[i]);
        }
    }
}
