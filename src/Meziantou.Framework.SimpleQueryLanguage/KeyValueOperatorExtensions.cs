namespace Meziantou.Framework.SimpleQueryLanguage;

internal static class KeyValueOperatorExtensions
{
    /// <summary>Gets the query syntax of the operator, used to turn an unhandled key-value query back into free text.</summary>
    public static string ToQueryText(this KeyValueOperator op)
    {
        return op switch
        {
            KeyValueOperator.EqualTo => ":",
            KeyValueOperator.NotEqualTo => "<>",
            KeyValueOperator.LessThan => "<",
            KeyValueOperator.LessThanOrEqual => "<=",
            KeyValueOperator.GreaterThan => ">",
            KeyValueOperator.GreaterThanOrEqual => ">=",
            _ => throw new ArgumentOutOfRangeException(nameof(op), op, message: null),
        };
    }
}
