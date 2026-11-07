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
        ArgumentNullException.ThrowIfNull(text);

        var tokens = Lexer.Tokenize(text).Where(t => t.Kind != QuerySyntaxKind.WhitespaceToken);
        var parser = new Parser(tokens);
        try
        {
            return parser.Parse();
        }
        catch (InsufficientExecutionStackException ex)
        {
            throw new QueryTooComplexException("The query nests expressions too deeply to be parsed", ex);
        }
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
