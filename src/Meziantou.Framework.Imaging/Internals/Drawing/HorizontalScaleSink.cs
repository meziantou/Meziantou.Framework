namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Multiplies the horizontal coordinates of what it receives by a factor, for a target whose pixels are that many times narrower.</summary>
internal sealed class HorizontalScaleSink : PathSink
{
    private PathSink? _target;
    private double _factor = 1;

    public void Configure(PathSink target, double factor)
    {
        _target = target;
        _factor = factor;
    }

    public override void MoveTo(double x, double y) => _target!.MoveTo(x * _factor, y);

    public override void LineTo(double x, double y, bool smooth) => _target!.LineTo(x * _factor, y, smooth);

    public override void Close(bool smooth) => _target!.Close(smooth);

    public override void End() => _target!.End();
}
