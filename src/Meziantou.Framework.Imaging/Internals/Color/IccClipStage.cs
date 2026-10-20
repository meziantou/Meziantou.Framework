namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Clips the first components to [0, 1]; a component that is not a number becomes 0.</summary>
internal sealed class IccClipStage(int channelCount) : IccStage
{
    public override void Apply(Span<double> values)
    {
        for (var i = 0; i < channelCount; i++)
        {
            values[i] = IccCurve.Clip(values[i]);
        }
    }

    public override void ApplyBatch(Span<double> components, int count)
    {
        for (var channel = 0; channel < channelCount; channel++)
        {
            foreach (ref var value in GetComponent(components, channel, count))
            {
                value = IccCurve.Clip(value);
            }
        }
    }
}
