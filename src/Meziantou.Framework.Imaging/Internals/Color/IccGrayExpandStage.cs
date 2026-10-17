namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Expands one component to three by scaling it (a gray value to a neutral color of the connection space).</summary>
internal sealed class IccGrayExpandStage(double scale0, double scale1, double scale2) : IccStage
{
    public override void Apply(Span<double> values)
    {
        var value = values[0];
        values[0] = value * scale0;
        values[1] = value * scale1;
        values[2] = value * scale2;
    }
}
