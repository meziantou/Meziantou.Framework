namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A one-dimensional curve of a curve set element.</summary>
internal abstract class IccMpeCurve
{
    /// <summary>Gets the number of operations of an evaluation of the curve (see <see cref="IccMpeElement.Cost"/>).</summary>
    public virtual long Cost => 4;

    public abstract double Evaluate(double value);
}
