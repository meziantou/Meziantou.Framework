namespace Meziantou.Framework.SimpleQueryLanguage.Ranges;

internal sealed class BinaryRangeSyntax<T> : RangeSyntax<T>
{
    internal BinaryRangeSyntax(T lowerBound, bool lowerBoundIncluded, T upperBound, bool upperBoundIncluded)
    {
        LowerBound = lowerBound;
        LowerBoundIncluded = lowerBoundIncluded;
        UpperBound = upperBound;
        UpperBoundIncluded = upperBoundIncluded;
    }

    public T LowerBound { get; }
    public T UpperBound { get; }
    public bool LowerBoundIncluded { get; }
    public bool UpperBoundIncluded { get; }

    public override bool IsInRange(T value, IComparer<T>? comparer)
    {
        comparer ??= Comparer<T>.Default;
        var c1 = comparer.Compare(LowerBound, value);
        var c2 = comparer.Compare(value, UpperBound);
        return (LowerBoundIncluded ? c1 <= 0 : c1 < 0) && (UpperBoundIncluded ? c2 <= 0 : c2 < 0);
    }

    public override string ToString()
    {
        return $"{(LowerBoundIncluded ? "[" : "(")}{LowerBound}..{UpperBound}{(UpperBoundIncluded ? "]" : ")")}";
    }
}
