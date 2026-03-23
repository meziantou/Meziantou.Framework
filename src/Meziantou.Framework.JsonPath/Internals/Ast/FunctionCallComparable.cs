namespace Meziantou.Framework.Json.Internals;

/// <summary>A function call expression used as a comparable (must have ValueType result).</summary>
internal sealed class FunctionCallComparable : Comparable
{
    public FunctionCallComparable(FunctionCallExpression functionCall)
    {
        FunctionCall = functionCall;
    }

    public override ComparableKind Kind => ComparableKind.FunctionCall;

    public FunctionCallExpression FunctionCall { get; }
}
