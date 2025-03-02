namespace Meziantou.Framework.SimpleQueryLanguage.Ranges;

public abstract class RangeSyntax<T>
{
    private protected RangeSyntax()
    {
    }

    public bool IsInRange(T value)
    {
        return IsInRange(value, comparer: null);
    }

    public abstract bool IsInRange(T value, IComparer<T>? comparer);
}
