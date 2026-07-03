using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.Json.Internals;

namespace Meziantou.Framework.Json;

/// <summary>
/// Represents a parsed JSONPath expression that can be evaluated against JSON values.
/// This class is immutable and thread-safe; a single instance can be reused to evaluate
/// multiple JSON documents.
/// </summary>
public sealed class JsonPath : IParsable<JsonPath>, ISpanParsable<JsonPath>
{
    private readonly string _expression;
    private readonly JsonPathExpression _ast;

    private JsonPath(string expression, JsonPathExpression ast)
    {
        _expression = expression;
        _ast = ast;
    }

    /// <summary>Parses a JSONPath expression.</summary>
    /// <param name="expression">A valid JSONPath expression string (must start with <c>$</c>).</param>
    /// <returns>A parsed <see cref="JsonPath"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The expression is not a well-formed or valid JSONPath query.</exception>
    public static JsonPath Parse(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return Parse(expression.AsSpan(), expression);
    }

    /// <summary>Parses a JSONPath expression from a character span.</summary>
    /// <param name="expression">A valid JSONPath expression (must start with <c>$</c>).</param>
    /// <returns>A parsed <see cref="JsonPath"/> instance.</returns>
    /// <exception cref="FormatException">The expression is not a well-formed or valid JSONPath query.</exception>
    public static JsonPath Parse(ReadOnlySpan<char> expression)
    {
        return Parse(expression, originalString: null);
    }

    /// <summary>Tries to parse a JSONPath expression.</summary>
    /// <param name="expression">A JSONPath expression string.</param>
    /// <param name="result">When this method returns, contains the parsed <see cref="JsonPath"/> if parsing succeeded, or <see langword="null"/> if it failed.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(string? expression, [NotNullWhen(true)] out JsonPath? result)
    {
        if (expression is null)
        {
            result = null;
            return false;
        }

        return TryParse(expression.AsSpan(), expression, out result);
    }

    /// <summary>Tries to parse a JSONPath expression from a character span.</summary>
    /// <param name="expression">A JSONPath expression.</param>
    /// <param name="result">When this method returns, contains the parsed <see cref="JsonPath"/> if parsing succeeded, or <see langword="null"/> if it failed.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<char> expression, [NotNullWhen(true)] out JsonPath? result)
    {
        return TryParse(expression, originalString: null, out result);
    }

    /// <summary>Evaluates this JSONPath expression against a custom object model.</summary>
    /// <typeparam name="TValue">The node type used by the JSONPath navigator.</typeparam>
    /// <param name="root">The root value to query. May be <see langword="null"/> when the root value is JSON <c>null</c>.</param>
    /// <param name="navigator">The navigator used to inspect values.</param>
    /// <returns>A <see cref="JsonPathResult{TValue}"/> containing all matched nodes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="navigator"/> is <see langword="null"/>.</exception>
    public JsonPathResult<TValue> Evaluate<TValue>(TValue? root, JsonPathNavigator<TValue> navigator)
    {
        return Evaluate(root, navigator, JsonPathEvaluationMode.Lax);
    }

    /// <summary>Evaluates this JSONPath expression against a custom object model.</summary>
    /// <typeparam name="TValue">The node type used by the JSONPath navigator.</typeparam>
    /// <param name="root">The root value to query. May be <see langword="null"/> when the root value is JSON <c>null</c>.</param>
    /// <param name="navigator">The navigator used to inspect values.</param>
    /// <param name="mode">The evaluation mode.</param>
    /// <returns>A <see cref="JsonPathResult{TValue}"/> containing all matched nodes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="navigator"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonPathEvaluationException">The path cannot be evaluated in <see cref="JsonPathEvaluationMode.Strict"/> mode.</exception>
    public JsonPathResult<TValue> Evaluate<TValue>(TValue? root, JsonPathNavigator<TValue> navigator, JsonPathEvaluationMode mode)
    {
        ArgumentNullException.ThrowIfNull(navigator);

        return JsonPathEvaluator.Evaluate(_ast, root, navigator, mode);
    }

    /// <summary>Evaluates this JSONPath expression against a JSON value.</summary>
    /// <param name="root">The root JSON value to query. May be <see langword="null"/>.</param>
    /// <returns>A <see cref="JsonPathResult"/> containing all matched nodes.</returns>
    public JsonPathResult Evaluate(JsonNode? root)
    {
        return Evaluate(root, JsonPathEvaluationMode.Lax);
    }

    /// <summary>Evaluates this JSONPath expression against a JSON value.</summary>
    /// <param name="root">The root JSON value to query. May be <see langword="null"/>.</param>
    /// <param name="mode">The evaluation mode.</param>
    /// <returns>A <see cref="JsonPathResult"/> containing all matched nodes.</returns>
    /// <exception cref="JsonPathEvaluationException">The path cannot be evaluated in <see cref="JsonPathEvaluationMode.Strict"/> mode.</exception>
    public JsonPathResult Evaluate(JsonNode? root, JsonPathEvaluationMode mode)
    {
        return JsonPathEvaluator.Evaluate(_ast, root, mode);
    }

    /// <summary>Evaluates this JSONPath expression against a JSON document.</summary>
    /// <param name="root">The root JSON document to query. May be <see langword="null"/>.</param>
    /// <returns>A <see cref="JsonPathResult{TValue}"/> containing all matched nodes.</returns>
    public JsonPathResult<JsonElement> Evaluate(JsonDocument? root)
    {
        return Evaluate(root, JsonPathEvaluationMode.Lax);
    }

    /// <summary>Evaluates this JSONPath expression against a JSON document.</summary>
    /// <param name="root">The root JSON document to query. May be <see langword="null"/>.</param>
    /// <param name="mode">The evaluation mode.</param>
    /// <returns>A <see cref="JsonPathResult{TValue}"/> containing all matched nodes.</returns>
    /// <exception cref="JsonPathEvaluationException">The path cannot be evaluated in <see cref="JsonPathEvaluationMode.Strict"/> mode.</exception>
    public JsonPathResult<JsonElement> Evaluate(JsonDocument? root, JsonPathEvaluationMode mode)
    {
        var element = root is null ? default : root.RootElement;
        return JsonPathEvaluator.Evaluate(_ast, element, JsonElementNavigator.Instance, mode);
    }

    /// <summary>Evaluates this JSONPath expression against a JSON element.</summary>
    /// <param name="root">The root JSON element to query.</param>
    /// <returns>A <see cref="JsonPathResult{TValue}"/> containing all matched nodes.</returns>
    public JsonPathResult<JsonElement> Evaluate(JsonElement root)
    {
        return Evaluate(root, JsonPathEvaluationMode.Lax);
    }

    /// <summary>Evaluates this JSONPath expression against a JSON element.</summary>
    /// <param name="root">The root JSON element to query.</param>
    /// <param name="mode">The evaluation mode.</param>
    /// <returns>A <see cref="JsonPathResult{TValue}"/> containing all matched nodes.</returns>
    /// <exception cref="JsonPathEvaluationException">The path cannot be evaluated in <see cref="JsonPathEvaluationMode.Strict"/> mode.</exception>
    public JsonPathResult<JsonElement> Evaluate(JsonElement root, JsonPathEvaluationMode mode)
    {
        return JsonPathEvaluator.Evaluate(_ast, root, JsonElementNavigator.Instance, mode);
    }

    /// <summary>
    /// Evaluates this JSONPath expression and returns the first matched custom value, or <see langword="null"/> when there is no match.
    /// </summary>
    /// <typeparam name="TValue">The node type used by the JSONPath navigator.</typeparam>
    /// <param name="root">The root value to query. May be <see langword="null"/> when the root value is JSON <c>null</c>.</param>
    /// <param name="navigator">The navigator used to inspect values.</param>
    /// <returns>The first matched value, or <see langword="null"/> when there is no match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="navigator"/> is <see langword="null"/>.</exception>
    public TValue? EvaluateValue<TValue>(TValue? root, JsonPathNavigator<TValue> navigator)
    {
        return EvaluateValue(root, navigator, JsonPathEvaluationMode.Lax);
    }

    /// <summary>
    /// Evaluates this JSONPath expression and returns the first matched custom value, or <see langword="null"/> when there is no match.
    /// </summary>
    /// <typeparam name="TValue">The node type used by the JSONPath navigator.</typeparam>
    /// <param name="root">The root value to query. May be <see langword="null"/> when the root value is JSON <c>null</c>.</param>
    /// <param name="navigator">The navigator used to inspect values.</param>
    /// <param name="mode">The evaluation mode.</param>
    /// <returns>The first matched value, or <see langword="null"/> when there is no match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="navigator"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonPathEvaluationException">The path cannot be evaluated in <see cref="JsonPathEvaluationMode.Strict"/> mode.</exception>
    public TValue? EvaluateValue<TValue>(TValue? root, JsonPathNavigator<TValue> navigator, JsonPathEvaluationMode mode)
    {
        var result = Evaluate(root, navigator, mode);
        return result.Count > 0 ? result[0].Value : default;
    }

    /// <summary>
    /// Evaluates this JSONPath expression and returns the first matched value, or <see langword="null"/> when there is no match.
    /// </summary>
    /// <param name="root">The root JSON value to query. May be <see langword="null"/>.</param>
    /// <returns>The first matched value, or <see langword="null"/> when there is no match.</returns>
    public JsonNode? EvaluateValue(JsonNode? root)
    {
        return EvaluateValue(root, JsonPathEvaluationMode.Lax);
    }

    /// <summary>
    /// Evaluates this JSONPath expression and returns the first matched value, or <see langword="null"/> when there is no match.
    /// </summary>
    /// <param name="root">The root JSON value to query. May be <see langword="null"/>.</param>
    /// <param name="mode">The evaluation mode.</param>
    /// <returns>The first matched value, or <see langword="null"/> when there is no match.</returns>
    /// <exception cref="JsonPathEvaluationException">The path cannot be evaluated in <see cref="JsonPathEvaluationMode.Strict"/> mode.</exception>
    public JsonNode? EvaluateValue(JsonNode? root, JsonPathEvaluationMode mode)
    {
        var result = Evaluate(root, mode);
        return result.Count > 0 ? result[0].Value : null;
    }

    /// <summary>
    /// Evaluates this JSONPath expression and returns the first matched value, or <see langword="null"/> when there is no match.
    /// </summary>
    /// <param name="root">The root JSON document to query. May be <see langword="null"/>.</param>
    /// <returns>The first matched value, or <see langword="null"/> when there is no match.</returns>
    public JsonElement? EvaluateValue(JsonDocument? root)
    {
        return EvaluateValue(root, JsonPathEvaluationMode.Lax);
    }

    /// <summary>
    /// Evaluates this JSONPath expression and returns the first matched value, or <see langword="null"/> when there is no match.
    /// </summary>
    /// <param name="root">The root JSON document to query. May be <see langword="null"/>.</param>
    /// <param name="mode">The evaluation mode.</param>
    /// <returns>The first matched value, or <see langword="null"/> when there is no match.</returns>
    /// <exception cref="JsonPathEvaluationException">The path cannot be evaluated in <see cref="JsonPathEvaluationMode.Strict"/> mode.</exception>
    public JsonElement? EvaluateValue(JsonDocument? root, JsonPathEvaluationMode mode)
    {
        var result = Evaluate(root, mode);
        return result.Count > 0 ? result[0].Value : null;
    }

    /// <summary>
    /// Evaluates this JSONPath expression and returns the first matched value, or <see langword="null"/> when there is no match.
    /// </summary>
    /// <param name="root">The root JSON element to query.</param>
    /// <returns>The first matched value, or <see langword="null"/> when there is no match.</returns>
    public JsonElement? EvaluateValue(JsonElement root)
    {
        return EvaluateValue(root, JsonPathEvaluationMode.Lax);
    }

    /// <summary>
    /// Evaluates this JSONPath expression and returns the first matched value, or <see langword="null"/> when there is no match.
    /// </summary>
    /// <param name="root">The root JSON element to query.</param>
    /// <param name="mode">The evaluation mode.</param>
    /// <returns>The first matched value, or <see langword="null"/> when there is no match.</returns>
    /// <exception cref="JsonPathEvaluationException">The path cannot be evaluated in <see cref="JsonPathEvaluationMode.Strict"/> mode.</exception>
    public JsonElement? EvaluateValue(JsonElement root, JsonPathEvaluationMode mode)
    {
        var result = Evaluate(root, mode);
        return result.Count > 0 ? result[0].Value : null;
    }

    /// <summary>Returns the original JSONPath expression string.</summary>
    public override string ToString() => _expression;

    static JsonPath IParsable<JsonPath>.Parse(string s, IFormatProvider? provider)
    {
        return Parse(s);
    }

    static bool IParsable<JsonPath>.TryParse(string? s, IFormatProvider? provider, [NotNullWhen(true)] out JsonPath? result)
    {
        return TryParse(s, out result);
    }

    static JsonPath ISpanParsable<JsonPath>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        return Parse(s);
    }

    static bool ISpanParsable<JsonPath>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, [NotNullWhen(true)] out JsonPath? result)
    {
        return TryParse(s, out result);
    }

    private static JsonPath Parse(ReadOnlySpan<char> expression, string? originalString)
    {
        var ast = JsonPathParser.Parse(expression);
        return new JsonPath(originalString ?? expression.ToString(), ast);
    }

    private static bool TryParse(ReadOnlySpan<char> expression, string? originalString, [NotNullWhen(true)] out JsonPath? result)
    {
        if (!JsonPathParser.TryParse(expression, out var ast))
        {
            result = null;
            return false;
        }

        result = new JsonPath(originalString ?? expression.ToString(), ast);
        return true;
    }
}
