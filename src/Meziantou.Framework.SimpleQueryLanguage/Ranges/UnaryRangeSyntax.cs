namespace Meziantou.Framework.SimpleQueryLanguage.Ranges;

internal sealed class UnaryRangeSyntax<T> : RangeSyntax<T>
{
    internal UnaryRangeSyntax(KeyValueOperator op, T operand)
    {
        Operator = op;
        Operand = operand;
    }

    public KeyValueOperator Operator { get; }

    public T Operand { get; }

    public override bool IsInRange(T value, IComparer<T>? comparer)
    {
        comparer ??= Comparer<T>.Default;
        var c = comparer.Compare(value, Operand);
        return Operator switch
        {
            KeyValueOperator.EqualTo => c == 0,
            KeyValueOperator.NotEqualTo => c != 0,
            KeyValueOperator.LessThan => c < 0,
            KeyValueOperator.LessThanOrEqual => c <= 0,
            KeyValueOperator.GreaterThan => c > 0,
            KeyValueOperator.GreaterThanOrEqual => c >= 0,
            _ => throw new InvalidOperationException($"Unexpected operator {Operator}"),
        };
    }

    public override string ToString()
    {
        return Operator switch
        {
            KeyValueOperator.EqualTo => $"{Operand}",
            KeyValueOperator.NotEqualTo => $"<>{Operand}",
            KeyValueOperator.LessThan => $"<{Operand}",
            KeyValueOperator.LessThanOrEqual => $"<={Operand}",
            KeyValueOperator.GreaterThan => $">{Operand}",
            KeyValueOperator.GreaterThanOrEqual => $">={Operand}",
            _ => throw new InvalidOperationException($"Unexpected operator {Operator}"),
        };
    }
}
