namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Clips the first components to [0, 1]; a component that is not a number becomes 0.</summary>
internal sealed class IccClipStage(int count) : IccStage
{
    public override void Apply(Span<double> values)
    {
        for (var i = 0; i < count; i++)
        {
            values[i] = IccCurve.Clip(values[i]);
        }
    }
}
