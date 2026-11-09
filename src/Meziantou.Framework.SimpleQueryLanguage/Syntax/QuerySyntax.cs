using System.CodeDom.Compiler;

namespace Meziantou.Framework.SimpleQueryLanguage.Syntax;

/// <summary>Represents a query syntax node in the abstract syntax tree.</summary>
public abstract partial class QuerySyntax : QueryNodeOrToken
{
    /// <summary>Parses a query string into a syntax tree.</summary>
    /// <param name="text">The query string to parse.</param>
    /// <returns>The root node of the parsed syntax tree.</returns>
    /// <exception cref="QueryTooComplexException">The query nests expressions too deeply to be parsed.</exception>
    public static QuerySyntax Parse(string text)
    {
        return Parse(text, out _);
    }

    /// <summary>Parses a query string into a syntax tree, and reports the problems found in it.</summary>
    /// <param name="text">The query string to parse.</param>
    /// <param name="diagnostics">
    /// When this method returns, the problems found in the query, such as unbalanced parentheses or operators without
    /// operand, in the order they appear in it. The list is empty when the query has no problem.
    /// </param>
    /// <returns>The root node of the parsed syntax tree. It is the same tree as <see cref="Parse(string)"/> returns.</returns>
    /// <exception cref="QueryTooComplexException">The query nests expressions too deeply to be parsed.</exception>
    /// <remarks>
    /// Keys are not checked: only a builder knows which keys have a handler. Use <see cref="Query{T}.Diagnostics"/> or
    /// <see cref="ExpressionQuery{T}.Diagnostics"/> to get the unknown keys too.
    /// </remarks>
    public static QuerySyntax Parse(string text, out IReadOnlyList<QueryDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(text);

        var tokens = Lexer.Tokenize(text).Where(t => t.Kind != QuerySyntaxKind.WhitespaceToken);
        var parser = new Parser(tokens);
        try
        {
            var result = parser.Parse();
            diagnostics = parser.Diagnostics;
            return result;
        }
        catch (InsufficientExecutionStackException ex)
        {
            throw new QueryTooComplexException("The query nests expressions too deeply to be parsed", ex);
        }
    }

    /// <summary>
    /// Adds a diagnostic for each key-value query of the tree whose key <paramref name="isKnownKey"/> rejects, and
    /// returns all the diagnostics in the order they appear in the query.
    /// </summary>
    internal static IReadOnlyList<QueryDiagnostic> AddUnknownKeyDiagnostics(QuerySyntax root, IReadOnlyList<QueryDiagnostic> diagnostics, Func<string, bool> isKnownKey)
    {
        List<QueryDiagnostic>? unknownKeys = null;

        // Walk with an explicit stack: a long conjunction is a left-leaning tree as deep as its term count
        var stack = new Stack<QuerySyntax>();
        stack.Push(root);
        while (stack.TryPop(out var node))
        {
            switch (node)
            {
                case KeyValueQuerySyntax keyValue:
                    if (!isKnownKey(keyValue.KeyToken.Text))
                    {
                        unknownKeys ??= [];
                        unknownKeys.Add(QueryDiagnostic.UnknownKey(keyValue.KeyToken));
                    }

                    break;

                case AndQuerySyntax andQuery:
                    stack.Push(andQuery.Right);
                    stack.Push(andQuery.Left);
                    break;

                case OrQuerySyntax orQuery:
                    stack.Push(orQuery.Right);
                    stack.Push(orQuery.Left);
                    break;

                case NegatedQuerySyntax negatedQuery:
                    stack.Push(negatedQuery.Query);
                    break;

                case ParenthesizedQuerySyntax parenthesizedQuery:
                    stack.Push(parenthesizedQuery.Query);
                    break;
            }
        }

        if (unknownKeys is null)
            return diagnostics;

        return [.. diagnostics.Concat(unknownKeys).OrderBy(diagnostic => diagnostic.Span.Start)];
    }

    public override string ToString()
    {
        using var stringWriter = new StringWriter();
        using (var writer = new IndentedTextWriter(stringWriter))
        {
            // Walk with an explicit stack: a long conjunction is a left-leaning tree as deep as its term count,
            // so recursing would overflow the stack. An entry holds either a node to write or a closing parenthesis.
            var stack = new Stack<(QuerySyntax? Node, int Indent)>();
            stack.Push((this, 0));

            while (stack.TryPop(out var item))
            {
                writer.Indent = item.Indent;
                switch (item.Node)
                {
                    case null:
                        writer.WriteLine(")");
                        break;

                    case TextQuerySyntax node:
                        writer.WriteLine(node.TextToken);
                        break;

                    case KeyValueQuerySyntax node:
                        writer.Write(node.KeyToken);
                        writer.Write(" ");
                        writer.Write(node.OperatorToken);
                        writer.Write(" ");
                        writer.Write(node.ValueToken);
                        writer.WriteLine();
                        break;

                    case OrQuerySyntax node:
                        writer.WriteLine("OR");
                        stack.Push((node.Right, item.Indent + 1));
                        stack.Push((node.Left, item.Indent + 1));
                        break;

                    case AndQuerySyntax node:
                        writer.WriteLine("AND");
                        stack.Push((node.Right, item.Indent + 1));
                        stack.Push((node.Left, item.Indent + 1));
                        break;

                    case NegatedQuerySyntax node:
                        writer.WriteLine("NOT");
                        stack.Push((node.Query, item.Indent + 1));
                        break;

                    case ParenthesizedQuerySyntax node:
                        writer.WriteLine("(");
                        stack.Push((null, item.Indent));
                        stack.Push((node.Query, item.Indent + 1));
                        break;
                }
            }
        }

        return stringWriter.ToString();
    }
}
