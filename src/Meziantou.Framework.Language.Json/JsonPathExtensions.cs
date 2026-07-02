using Meziantou.Framework.Json;
using Meziantou.Framework.Language.Json.Internals;

namespace Meziantou.Framework.Language.Json;

/// <summary>Provides JSONPath evaluation helpers for JSON syntax trees.</summary>
public static class JsonPathExtensions
{
    /// <summary>Evaluates this JSONPath expression against a JSON syntax tree.</summary>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <param name="root">The root JSON syntax tree to query. May be <see langword="null"/>.</param>
    /// <returns>A <see cref="JsonPathResult{TValue}"/> containing all matched syntax nodes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static JsonPathResult<JsonSyntaxNode> Evaluate(this JsonPath path, JsonSyntaxTree? root)
    {
        return Evaluate(path, root, JsonPathEvaluationMode.Lax);
    }

    /// <summary>Evaluates this JSONPath expression against a JSON syntax tree.</summary>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <param name="root">The root JSON syntax tree to query. May be <see langword="null"/>.</param>
    /// <param name="mode">The evaluation mode.</param>
    /// <returns>A <see cref="JsonPathResult{TValue}"/> containing all matched syntax nodes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonPathEvaluationException">The path cannot be evaluated in <see cref="JsonPathEvaluationMode.Strict"/> mode.</exception>
    public static JsonPathResult<JsonSyntaxNode> Evaluate(this JsonPath path, JsonSyntaxTree? root, JsonPathEvaluationMode mode)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.Evaluate(GetJsonSyntaxRoot(root), JsonSyntaxNodeNavigator.Instance, mode);
    }

    /// <summary>Evaluates a JSONPath expression against this JSON syntax tree.</summary>
    /// <param name="root">The root JSON syntax tree to query. May be <see langword="null"/>.</param>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <returns>A <see cref="JsonPathResult{TValue}"/> containing all matched syntax nodes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static JsonPathResult<JsonSyntaxNode> Evaluate(this JsonSyntaxTree? root, JsonPath path)
    {
        return Evaluate(path, root, JsonPathEvaluationMode.Lax);
    }

    /// <summary>Evaluates a JSONPath expression against this JSON syntax tree.</summary>
    /// <param name="root">The root JSON syntax tree to query. May be <see langword="null"/>.</param>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <param name="mode">The evaluation mode.</param>
    /// <returns>A <see cref="JsonPathResult{TValue}"/> containing all matched syntax nodes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonPathEvaluationException">The path cannot be evaluated in <see cref="JsonPathEvaluationMode.Strict"/> mode.</exception>
    public static JsonPathResult<JsonSyntaxNode> Evaluate(this JsonSyntaxTree? root, JsonPath path, JsonPathEvaluationMode mode)
    {
        return Evaluate(path, root, mode);
    }

    /// <summary>Evaluates this JSONPath expression against a JSON syntax node.</summary>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <param name="root">The root JSON syntax node to query. May be <see langword="null"/> when the root value is JSON <c>null</c>.</param>
    /// <returns>A <see cref="JsonPathResult{TValue}"/> containing all matched syntax nodes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static JsonPathResult<JsonSyntaxNode> Evaluate(this JsonPath path, JsonSyntaxNode? root)
    {
        return Evaluate(path, root, JsonPathEvaluationMode.Lax);
    }

    /// <summary>Evaluates this JSONPath expression against a JSON syntax node.</summary>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <param name="root">The root JSON syntax node to query. May be <see langword="null"/> when the root value is JSON <c>null</c>.</param>
    /// <param name="mode">The evaluation mode.</param>
    /// <returns>A <see cref="JsonPathResult{TValue}"/> containing all matched syntax nodes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonPathEvaluationException">The path cannot be evaluated in <see cref="JsonPathEvaluationMode.Strict"/> mode.</exception>
    public static JsonPathResult<JsonSyntaxNode> Evaluate(this JsonPath path, JsonSyntaxNode? root, JsonPathEvaluationMode mode)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.Evaluate(GetJsonSyntaxRoot(root), JsonSyntaxNodeNavigator.Instance, mode);
    }

    /// <summary>Evaluates a JSONPath expression against this JSON syntax node.</summary>
    /// <param name="root">The root JSON syntax node to query. May be <see langword="null"/> when the root value is JSON <c>null</c>.</param>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <returns>A <see cref="JsonPathResult{TValue}"/> containing all matched syntax nodes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static JsonPathResult<JsonSyntaxNode> Evaluate(this JsonSyntaxNode? root, JsonPath path)
    {
        return Evaluate(path, root, JsonPathEvaluationMode.Lax);
    }

    /// <summary>Evaluates a JSONPath expression against this JSON syntax node.</summary>
    /// <param name="root">The root JSON syntax node to query. May be <see langword="null"/> when the root value is JSON <c>null</c>.</param>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <param name="mode">The evaluation mode.</param>
    /// <returns>A <see cref="JsonPathResult{TValue}"/> containing all matched syntax nodes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonPathEvaluationException">The path cannot be evaluated in <see cref="JsonPathEvaluationMode.Strict"/> mode.</exception>
    public static JsonPathResult<JsonSyntaxNode> Evaluate(this JsonSyntaxNode? root, JsonPath path, JsonPathEvaluationMode mode)
    {
        return Evaluate(path, root, mode);
    }

    /// <summary>
    /// Evaluates this JSONPath expression and returns the first matched syntax node, or <see langword="null"/> when there is no match.
    /// </summary>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <param name="root">The root JSON syntax tree to query. May be <see langword="null"/>.</param>
    /// <returns>The first matched syntax node, or <see langword="null"/> when there is no match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static JsonSyntaxNode? EvaluateValue(this JsonPath path, JsonSyntaxTree? root)
    {
        return EvaluateValue(path, root, JsonPathEvaluationMode.Lax);
    }

    /// <summary>
    /// Evaluates this JSONPath expression and returns the first matched syntax node, or <see langword="null"/> when there is no match.
    /// </summary>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <param name="root">The root JSON syntax tree to query. May be <see langword="null"/>.</param>
    /// <param name="mode">The evaluation mode.</param>
    /// <returns>The first matched syntax node, or <see langword="null"/> when there is no match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonPathEvaluationException">The path cannot be evaluated in <see cref="JsonPathEvaluationMode.Strict"/> mode.</exception>
    public static JsonSyntaxNode? EvaluateValue(this JsonPath path, JsonSyntaxTree? root, JsonPathEvaluationMode mode)
    {
        var result = Evaluate(path, root, mode);
        return result.Count > 0 ? result[0].Value : null;
    }

    /// <summary>
    /// Evaluates a JSONPath expression against this JSON syntax tree and returns the first matched syntax node, or <see langword="null"/> when there is no match.
    /// </summary>
    /// <param name="root">The root JSON syntax tree to query. May be <see langword="null"/>.</param>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <returns>The first matched syntax node, or <see langword="null"/> when there is no match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static JsonSyntaxNode? EvaluateValue(this JsonSyntaxTree? root, JsonPath path)
    {
        return EvaluateValue(path, root, JsonPathEvaluationMode.Lax);
    }

    /// <summary>
    /// Evaluates a JSONPath expression against this JSON syntax tree and returns the first matched syntax node, or <see langword="null"/> when there is no match.
    /// </summary>
    /// <param name="root">The root JSON syntax tree to query. May be <see langword="null"/>.</param>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <param name="mode">The evaluation mode.</param>
    /// <returns>The first matched syntax node, or <see langword="null"/> when there is no match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonPathEvaluationException">The path cannot be evaluated in <see cref="JsonPathEvaluationMode.Strict"/> mode.</exception>
    public static JsonSyntaxNode? EvaluateValue(this JsonSyntaxTree? root, JsonPath path, JsonPathEvaluationMode mode)
    {
        return EvaluateValue(path, root, mode);
    }

    /// <summary>
    /// Evaluates this JSONPath expression and returns the first matched syntax node, or <see langword="null"/> when there is no match.
    /// </summary>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <param name="root">The root JSON syntax node to query. May be <see langword="null"/> when the root value is JSON <c>null</c>.</param>
    /// <returns>The first matched syntax node, or <see langword="null"/> when there is no match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static JsonSyntaxNode? EvaluateValue(this JsonPath path, JsonSyntaxNode? root)
    {
        return EvaluateValue(path, root, JsonPathEvaluationMode.Lax);
    }

    /// <summary>
    /// Evaluates this JSONPath expression and returns the first matched syntax node, or <see langword="null"/> when there is no match.
    /// </summary>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <param name="root">The root JSON syntax node to query. May be <see langword="null"/> when the root value is JSON <c>null</c>.</param>
    /// <param name="mode">The evaluation mode.</param>
    /// <returns>The first matched syntax node, or <see langword="null"/> when there is no match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonPathEvaluationException">The path cannot be evaluated in <see cref="JsonPathEvaluationMode.Strict"/> mode.</exception>
    public static JsonSyntaxNode? EvaluateValue(this JsonPath path, JsonSyntaxNode? root, JsonPathEvaluationMode mode)
    {
        var result = Evaluate(path, root, mode);
        return result.Count > 0 ? result[0].Value : null;
    }

    /// <summary>
    /// Evaluates a JSONPath expression against this JSON syntax node and returns the first matched syntax node, or <see langword="null"/> when there is no match.
    /// </summary>
    /// <param name="root">The root JSON syntax node to query. May be <see langword="null"/> when the root value is JSON <c>null</c>.</param>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <returns>The first matched syntax node, or <see langword="null"/> when there is no match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static JsonSyntaxNode? EvaluateValue(this JsonSyntaxNode? root, JsonPath path)
    {
        return EvaluateValue(path, root, JsonPathEvaluationMode.Lax);
    }

    /// <summary>
    /// Evaluates a JSONPath expression against this JSON syntax node and returns the first matched syntax node, or <see langword="null"/> when there is no match.
    /// </summary>
    /// <param name="root">The root JSON syntax node to query. May be <see langword="null"/> when the root value is JSON <c>null</c>.</param>
    /// <param name="path">The JSONPath expression to evaluate.</param>
    /// <param name="mode">The evaluation mode.</param>
    /// <returns>The first matched syntax node, or <see langword="null"/> when there is no match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonPathEvaluationException">The path cannot be evaluated in <see cref="JsonPathEvaluationMode.Strict"/> mode.</exception>
    public static JsonSyntaxNode? EvaluateValue(this JsonSyntaxNode? root, JsonPath path, JsonPathEvaluationMode mode)
    {
        return EvaluateValue(path, root, mode);
    }

    private static JsonValueSyntax? GetJsonSyntaxRoot(JsonSyntaxTree? root)
    {
        return root?.Root.Value;
    }

    private static JsonSyntaxNode? GetJsonSyntaxRoot(JsonSyntaxNode? root)
    {
        return root is JsonDocumentSyntax document ? document.Value : root;
    }
}
