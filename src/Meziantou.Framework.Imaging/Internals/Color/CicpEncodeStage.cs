namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The last stage of a conversion whose destination is a CICP color space: encodes each linear value with the function
/// of the transfer characteristics (<see cref="CicpTransfer.Encode"/>, which gives no light to a negative value and
/// clips the sample to [0, 1]: a color outside the color space is clipped component by component).
/// </summary>
internal sealed class CicpEncodeStage(byte transfer, int channelCount) : IccStage
{
    public override void Apply(Span<double> values)
    {
        for (var i = 0; i < channelCount; i++)
        {
            values[i] = CicpTransfer.Encode(transfer, values[i]);
        }
    }
}
