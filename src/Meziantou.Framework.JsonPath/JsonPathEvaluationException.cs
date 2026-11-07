namespace Meziantou.Framework.Json;

/// <summary>
/// The exception that is thrown when JSONPath evaluation fails.
/// </summary>
/// <remarks>
/// This is thrown for a path error in <see cref="JsonPathEvaluationMode.Strict"/> mode, and — in either mode —
/// when a value is nested more deeply than the evaluator will recurse, which also covers a
/// <see cref="JsonPathNavigator{TValue}"/> that exposes a cycle.
/// </remarks>
public sealed class JsonPathEvaluationException : InvalidOperationException
{
    public JsonPathEvaluationException()
    {
    }

    public JsonPathEvaluationException(string message)
        : base(message)
    {
    }

    public JsonPathEvaluationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
