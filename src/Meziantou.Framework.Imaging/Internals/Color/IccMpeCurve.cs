namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A one-dimensional curve of a curve set element.</summary>
internal abstract class IccMpeCurve
{
    public abstract double Evaluate(double value);
}
