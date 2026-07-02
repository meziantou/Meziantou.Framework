using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.Json;

/// <summary>Provides JSONPath evaluation helpers for JSON values.</summary>
public static class JsonPathExtensions
{
    /// <summary>Evaluates a JSONPath expression against this JSON value.</summary>
    /// <param name="root">The root JSON value to query. May be <see langword="null"/>.</param>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <returns>A <see cref="JsonPathResult"/> containing all matched nodes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static JsonPathResult Evaluate(this JsonNode? root, JsonPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.Evaluate(root);
    }

    /// <summary>Evaluates a JSONPath expression against this JSON value.</summary>
    /// <param name="root">The root JSON value to query. May be <see langword="null"/>.</param>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <param name="mode">The evaluation mode.</param>
    /// <returns>A <see cref="JsonPathResult"/> containing all matched nodes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonPathEvaluationException">The path cannot be evaluated in <see cref="JsonPathEvaluationMode.Strict"/> mode.</exception>
    public static JsonPathResult Evaluate(this JsonNode? root, JsonPath path, JsonPathEvaluationMode mode)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.Evaluate(root, mode);
    }

    /// <summary>Evaluates a JSONPath expression against this JSON document.</summary>
    /// <param name="root">The root JSON document to query. May be <see langword="null"/>.</param>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <returns>A <see cref="JsonPathResult{TValue}"/> containing all matched nodes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static JsonPathResult<JsonElement> Evaluate(this JsonDocument? root, JsonPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.Evaluate(root);
    }

    /// <summary>Evaluates a JSONPath expression against this JSON document.</summary>
    /// <param name="root">The root JSON document to query. May be <see langword="null"/>.</param>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <param name="mode">The evaluation mode.</param>
    /// <returns>A <see cref="JsonPathResult{TValue}"/> containing all matched nodes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonPathEvaluationException">The path cannot be evaluated in <see cref="JsonPathEvaluationMode.Strict"/> mode.</exception>
    public static JsonPathResult<JsonElement> Evaluate(this JsonDocument? root, JsonPath path, JsonPathEvaluationMode mode)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.Evaluate(root, mode);
    }

    /// <summary>Evaluates a JSONPath expression against this JSON element.</summary>
    /// <param name="root">The root JSON element to query.</param>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <returns>A <see cref="JsonPathResult{TValue}"/> containing all matched nodes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static JsonPathResult<JsonElement> Evaluate(this JsonElement root, JsonPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.Evaluate(root);
    }

    /// <summary>Evaluates a JSONPath expression against this JSON element.</summary>
    /// <param name="root">The root JSON element to query.</param>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <param name="mode">The evaluation mode.</param>
    /// <returns>A <see cref="JsonPathResult{TValue}"/> containing all matched nodes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonPathEvaluationException">The path cannot be evaluated in <see cref="JsonPathEvaluationMode.Strict"/> mode.</exception>
    public static JsonPathResult<JsonElement> Evaluate(this JsonElement root, JsonPath path, JsonPathEvaluationMode mode)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.Evaluate(root, mode);
    }

    /// <summary>
    /// Evaluates a JSONPath expression against this JSON value and returns the first matched value, or <see langword="null"/> when there is no match.
    /// </summary>
    /// <param name="root">The root JSON value to query. May be <see langword="null"/>.</param>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <returns>The first matched value, or <see langword="null"/> when there is no match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static JsonNode? EvaluateValue(this JsonNode? root, JsonPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.EvaluateValue(root);
    }

    /// <summary>
    /// Evaluates a JSONPath expression against this JSON value and returns the first matched value, or <see langword="null"/> when there is no match.
    /// </summary>
    /// <param name="root">The root JSON value to query. May be <see langword="null"/>.</param>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <param name="mode">The evaluation mode.</param>
    /// <returns>The first matched value, or <see langword="null"/> when there is no match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonPathEvaluationException">The path cannot be evaluated in <see cref="JsonPathEvaluationMode.Strict"/> mode.</exception>
    public static JsonNode? EvaluateValue(this JsonNode? root, JsonPath path, JsonPathEvaluationMode mode)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.EvaluateValue(root, mode);
    }

    /// <summary>
    /// Evaluates a JSONPath expression against this JSON document and returns the first matched value, or <see langword="null"/> when there is no match.
    /// </summary>
    /// <param name="root">The root JSON document to query. May be <see langword="null"/>.</param>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <returns>The first matched value, or <see langword="null"/> when there is no match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static JsonElement? EvaluateValue(this JsonDocument? root, JsonPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.EvaluateValue(root);
    }

    /// <summary>
    /// Evaluates a JSONPath expression against this JSON document and returns the first matched value, or <see langword="null"/> when there is no match.
    /// </summary>
    /// <param name="root">The root JSON document to query. May be <see langword="null"/>.</param>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <param name="mode">The evaluation mode.</param>
    /// <returns>The first matched value, or <see langword="null"/> when there is no match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonPathEvaluationException">The path cannot be evaluated in <see cref="JsonPathEvaluationMode.Strict"/> mode.</exception>
    public static JsonElement? EvaluateValue(this JsonDocument? root, JsonPath path, JsonPathEvaluationMode mode)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.EvaluateValue(root, mode);
    }

    /// <summary>
    /// Evaluates a JSONPath expression against this JSON element and returns the first matched value, or <see langword="null"/> when there is no match.
    /// </summary>
    /// <param name="root">The root JSON element to query.</param>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <returns>The first matched value, or <see langword="null"/> when there is no match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static JsonElement? EvaluateValue(this JsonElement root, JsonPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.EvaluateValue(root);
    }

    /// <summary>
    /// Evaluates a JSONPath expression against this JSON element and returns the first matched value, or <see langword="null"/> when there is no match.
    /// </summary>
    /// <param name="root">The root JSON element to query.</param>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <param name="mode">The evaluation mode.</param>
    /// <returns>The first matched value, or <see langword="null"/> when there is no match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonPathEvaluationException">The path cannot be evaluated in <see cref="JsonPathEvaluationMode.Strict"/> mode.</exception>
    public static JsonElement? EvaluateValue(this JsonElement root, JsonPath path, JsonPathEvaluationMode mode)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.EvaluateValue(root, mode);
    }
}
