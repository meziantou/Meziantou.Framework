namespace Meziantou.Framework.SimpleQueryLanguage.Ranges;

internal sealed class UnaryRangeSyntax<T> : RangeSyntax<T>
{
    internal UnaryRangeSyntax(KeyValueOperator op, T operand)
    {
        Op = op;
        Operand = operand;
    }

    public KeyValueOperator Op { get; }

    public T Operand { get; }

    public override bool IsInRange(T value, IComparer<T>? comparer)
    {
        comparer ??= Comparer<T>.Default;
        var c = comparer.Compare(value, Operand);
        return Op switch
        {
            KeyValueOperator.EqualTo => c == 0,
            KeyValueOperator.NotEqualTo => c != 0,
            KeyValueOperator.LessThan => c < 0,
            KeyValueOperator.LessThanOrEqual => c <= 0,
            KeyValueOperator.GreaterThan => c > 0,
            KeyValueOperator.GreaterThanOrEqual => c >= 0,
            _ => throw new Exception($"Unexpected operator {Op}")
        };
    }

    public override string ToString()
    {
        return Op switch
        {
            KeyValueOperator.EqualTo => $"{Operand}",
            KeyValueOperator.NotEqualTo => $"<>{Operand}",
            KeyValueOperator.LessThan => $"<{Operand}",
            KeyValueOperator.LessThanOrEqual => $"<={Operand}",
            KeyValueOperator.GreaterThan => $">{Operand}",
            KeyValueOperator.GreaterThanOrEqual => $">={Operand}",
            _ => throw new Exception($"Unexpected operator {Op}")
        };
    }
}
