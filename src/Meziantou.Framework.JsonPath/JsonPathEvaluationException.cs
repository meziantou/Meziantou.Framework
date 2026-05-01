namespace Meziantou.Framework.Json;

/// <summary>
/// The exception that is thrown when JSONPath evaluation fails in <see cref="JsonPathEvaluationMode.Strict"/> mode.
/// </summary>
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
