namespace Meziantou.Framework.Globbing.Internals;

internal abstract class Segment
{
    public abstract bool IsMatch(ref PathReader pathReader);

    public virtual bool IsRecursiveMatchAll => false;
}
