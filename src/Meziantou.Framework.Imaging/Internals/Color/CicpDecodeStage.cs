namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The first stage of a conversion whose source is a CICP color space: decodes each sample to linear light with the
/// function of the transfer characteristics (<see cref="CicpTransfer.Decode"/>, which clips the sample to [0, 1]). The
/// light is relative to the nominal peak of the function and is not clipped: SMPTE ST 428-1 samples go above 1.
/// </summary>
internal sealed class CicpDecodeStage(byte transfer, int channelCount) : IccStage
{
    /// <summary>Gets the number of samples that the stage decodes.</summary>
    public int ChannelCount => channelCount;

    /// <summary>Decodes one sample.</summary>
    public double Evaluate(double value) => CicpTransfer.Decode(transfer, value);

    public override void Apply(Span<double> values)
    {
        for (var i = 0; i < channelCount; i++)
        {
            values[i] = CicpTransfer.Decode(transfer, values[i]);
        }
    }

    public override void ApplyBatch(Span<double> components, int count)
    {
        for (var channel = 0; channel < channelCount; channel++)
        {
            foreach (ref var value in GetComponent(components, channel, count))
            {
                value = CicpTransfer.Decode(transfer, value);
            }
        }
    }
}
