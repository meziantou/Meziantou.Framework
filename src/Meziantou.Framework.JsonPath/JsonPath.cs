using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.Json.Internals;

namespace Meziantou.Framework.Json;

/// <summary>
/// Represents a parsed JSONPath expression that can be evaluated against JSON values.
/// This class is immutable and thread-safe; a single instance can be reused to evaluate
/// multiple JSON documents.
/// </summary>
public sealed class JsonPath
#if NET7_0_OR_GREATER
    : IParsable<JsonPath>, ISpanParsable<JsonPath>
#endif
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

    /// <summary>Evaluates this JSONPath expression against a JSON value.</summary>
    /// <param name="root">The root JSON value to query. May be <see langword="null"/>.</param>
    /// <returns>A <see cref="JsonPathResult"/> containing all matched nodes.</returns>
    public JsonPathResult Evaluate(JsonNode? root)
    {
        return JsonPathEvaluator.Evaluate(_ast, root);
    }

    /// <summary>Evaluates this JSONPath expression against a JSON document.</summary>
    /// <param name="root">The root JSON document to query. May be <see langword="null"/>.</param>
    /// <returns>A <see cref="JsonPathResult"/> containing all matched nodes.</returns>
    public JsonPathResult Evaluate(JsonDocument? root)
    {
        JsonNode? node = null;
        if (root is not null)
        {
            var json = root.RootElement.GetRawText();
            node = JsonNode.Parse(json);
        }

        return JsonPathEvaluator.Evaluate(_ast, node);
    }

    /// <summary>Returns the original JSONPath expression string.</summary>
    public override string ToString() => _expression;

#if NET7_0_OR_GREATER
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
#endif

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
