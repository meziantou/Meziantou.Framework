namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The lookup of three tone curves, one per color channel.</summary>
internal sealed class ToneCurveLookup(ToneCurve redCurve, ToneCurve greenCurve, ToneCurve blueCurve) : ColorLookup
{
    public override bool IsPerChannel => true;

    public override double MapChannel(int channel, double value) => channel switch
    {
        0 => redCurve.Evaluate(value),
        1 => greenCurve.Evaluate(value),
        2 => blueCurve.Evaluate(value),
        _ => throw new ArgumentOutOfRangeException(nameof(channel)),
    };

    public override void MapColor(ref double red, ref double green, ref double blue)
    {
        red = redCurve.Evaluate(red);
        green = greenCurve.Evaluate(green);
        blue = blueCurve.Evaluate(blue);
    }
}
