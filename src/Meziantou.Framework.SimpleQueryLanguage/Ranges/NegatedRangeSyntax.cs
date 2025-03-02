namespace Meziantou.Framework.SimpleQueryLanguage.Ranges;

internal sealed class NegatedRangeSyntax<T> : RangeSyntax<T>
{
    internal NegatedRangeSyntax(RangeSyntax<T> operand)
    {
        Operand = operand;
    }

    public RangeSyntax<T> Operand { get; }

    public override bool IsInRange(T value, IComparer<T>? comparer)
    {
        return !Operand.IsInRange(value, comparer);
    }

    public override string ToString()
    {
        return $"not {Operand}";
    }
}
