namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A segment of a segmented curve.</summary>
internal abstract class IccMpeSegment
{
    public abstract double Evaluate(double value);
}
