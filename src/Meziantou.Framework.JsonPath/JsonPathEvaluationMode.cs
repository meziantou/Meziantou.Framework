namespace Meziantou.Framework.Json;

/// <summary>Controls how JSONPath evaluation handles path errors.</summary>
public enum JsonPathEvaluationMode
{
    /// <summary>
    /// Tolerant evaluation. Path errors (such as missing members or invalid indexes) produce no match.
    /// </summary>
    Lax,

    /// <summary>
    /// Strict evaluation. Path errors raise a <see cref="JsonPathEvaluationException"/>.
    /// </summary>
    Strict,
}
