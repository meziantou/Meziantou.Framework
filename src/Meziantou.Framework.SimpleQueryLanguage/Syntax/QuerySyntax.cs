using System.CodeDom.Compiler;

namespace Meziantou.Framework.SimpleQueryLanguage.Syntax;

public abstract partial class QuerySyntax : QueryNodeOrToken
{
    public static QuerySyntax Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var tokens = Lexer.Tokenize(text).Where(t => t.Kind != QuerySyntaxKind.WhitespaceToken);
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    public override string ToString()
    {
        using var stringWriter = new StringWriter();
        using (var indentedTextWriter = new IndentedTextWriter(stringWriter))
        {
            Walk(indentedTextWriter, this);
        }

        return stringWriter.ToString();

        static void Walk(IndentedTextWriter writer, QuerySyntax node)
        {
            switch (node.Kind)
            {
                case QuerySyntaxKind.TextQuery:
                    WalkTextExpression(writer, (TextQuerySyntax)node);
                    break;
                case QuerySyntaxKind.KeyValueQuery:
                    WalkKeyValueExpression(writer, (KeyValueQuerySyntax)node);
                    break;
                case QuerySyntaxKind.OrQuery:
                    WalkOrExpression(writer, (OrQuerySyntax)node);
                    break;
                case QuerySyntaxKind.AndQuery:
                    WalkAndExpression(writer, (AndQuerySyntax)node);
                    break;
                case QuerySyntaxKind.NegatedQuery:
                    WalkNegatedExpression(writer, (NegatedQuerySyntax)node);
                    break;
                case QuerySyntaxKind.ParenthesizedQuery:
                    WalkParenthesizedExpression(writer, (ParenthesizedQuerySyntax)node);
                    break;
            }
        }

        static void WalkTextExpression(IndentedTextWriter writer, TextQuerySyntax node)
        {
            writer.WriteLine(node.TextToken);
        }

        static void WalkKeyValueExpression(IndentedTextWriter writer, KeyValueQuerySyntax node)
        {
            writer.Write(node.KeyToken);
            writer.Write(" ");
            writer.Write(node.OperatorToken);
            writer.Write(" ");
            writer.Write(node.ValueToken);
            writer.WriteLine();
        }

        static void WalkOrExpression(IndentedTextWriter writer, OrQuerySyntax node)
        {
            writer.WriteLine("OR");

            writer.Indent++;
            Walk(writer, node.Left);
            Walk(writer, node.Right);
            writer.Indent--;
        }

        static void WalkAndExpression(IndentedTextWriter writer, AndQuerySyntax node)
        {
            writer.WriteLine("AND");

            writer.Indent++;
            Walk(writer, node.Left);
            Walk(writer, node.Right);
            writer.Indent--;
        }

        static void WalkNegatedExpression(IndentedTextWriter writer, NegatedQuerySyntax node)
        {
            writer.WriteLine("NOT");

            writer.Indent++;
            Walk(writer, node.Query);
            writer.Indent--;
        }

        static void WalkParenthesizedExpression(IndentedTextWriter writer, ParenthesizedQuerySyntax node)
        {
            writer.WriteLine("(");

            writer.Indent++;
            Walk(writer, node.Query);
            writer.Indent--;

            writer.WriteLine(")");
        }
    }
}
