using System.CodeDom.Compiler;
using System.Diagnostics;
using Meziantou.Framework.SimpleQueryLanguage.Syntax;

namespace Meziantou.Framework.SimpleQueryLanguage.Binding;

public abstract class BoundQuery
{
    public static IReadOnlyList<IReadOnlyList<BoundQuery>> Create(QuerySyntax syntax)
    {
        ArgumentNullException.ThrowIfNull(syntax);

        var result = CreateInternal(syntax);
        var dnf = ToDisjunctiveNormalForm(result);
        return Flatten(dnf);
    }

    private static BoundQuery CreateInternal(QuerySyntax syntax)
    {
        return syntax.Kind switch
        {
            QuerySyntaxKind.TextQuery => CreateTextExpression((TextQuerySyntax)syntax),
            QuerySyntaxKind.KeyValueQuery => CreateKeyValueExpression((KeyValueQuerySyntax)syntax),
            QuerySyntaxKind.OrQuery => CreateOrExpression((OrQuerySyntax)syntax),
            QuerySyntaxKind.AndQuery => CreateAndExpression((AndQuerySyntax)syntax),
            QuerySyntaxKind.NegatedQuery => CreateNegatedExpression((NegatedQuerySyntax)syntax),
            QuerySyntaxKind.ParenthesizedQuery => CreateParenthesizedExpression((ParenthesizedQuerySyntax)syntax),
            _ => throw new ArgumentOutOfRangeException(nameof(syntax), $"Unexpected node {syntax.Kind}"),
        };
    }

    private static BoundTextQuery CreateTextExpression(TextQuerySyntax node)
    {
        Debug.Assert(node.TextToken.Value is not null);

        return new BoundTextQuery(isNegated: false, node.TextToken.Value);
    }

    private static BoundKeyValueQuery CreateKeyValueExpression(KeyValueQuerySyntax node)
    {
        Debug.Assert(node.KeyToken.Value is not null);
        Debug.Assert(node.ValueToken.Value is not null);

        var key = node.KeyToken.Value;
        var value = node.ValueToken.Value;
        var op = node.OperatorToken.Kind switch
        {
            QuerySyntaxKind.ColonToken => KeyValueOperator.EqualTo,
            QuerySyntaxKind.EqualOperatorToken => KeyValueOperator.EqualTo,
            QuerySyntaxKind.NotEqualOperatorToken => KeyValueOperator.NotEqualTo,
            QuerySyntaxKind.LessThanOperatorToken => KeyValueOperator.LessThan,
            QuerySyntaxKind.LessThanOrEqualOperatorToken => KeyValueOperator.LessThanOrEqual,
            QuerySyntaxKind.GreaterThanOperatorToken => KeyValueOperator.GreaterThan,
            QuerySyntaxKind.GreaterThanOrEqualOperatorToken => KeyValueOperator.GreaterThanOrEqual,
            _ => throw new InvalidOperationException(),
        };

        return new BoundKeyValueQuery(isNegated: false, key, value, op);
    }

    private static BoundOrQuery CreateOrExpression(OrQuerySyntax node)
    {
        return new BoundOrQuery(CreateInternal(node.Left), CreateInternal(node.Right));
    }

    private static BoundAndQuery CreateAndExpression(AndQuerySyntax node)
    {
        return new BoundAndQuery(CreateInternal(node.Left), CreateInternal(node.Right));
    }

    private static BoundNegatedQuery CreateNegatedExpression(NegatedQuerySyntax node)
    {
        return new BoundNegatedQuery(CreateInternal(node.Query));
    }

    private static BoundQuery CreateParenthesizedExpression(ParenthesizedQuerySyntax node)
    {
        return CreateInternal(node.Query);
    }

    private static BoundQuery ToDisjunctiveNormalForm(BoundQuery node)
    {
        if (node is BoundNegatedQuery negated)
            return ToDisjunctiveNormalForm(Negate(negated.Query));

        if (node is BoundOrQuery or)
        {
            var left = ToDisjunctiveNormalForm(or.Left);
            var right = ToDisjunctiveNormalForm(or.Right);
            if (ReferenceEquals(left, or.Left) && ReferenceEquals(right, or.Right))
                return node;

            return new BoundOrQuery(left, right);
        }

        if (node is BoundAndQuery and)
        {
            var left = ToDisjunctiveNormalForm(and.Left);
            var right = ToDisjunctiveNormalForm(and.Right);

            // (A OR B) AND C      ->    (A AND C) OR (B AND C)

            if (left is BoundOrQuery leftOr)
            {
                var a = leftOr.Left;
                var b = leftOr.Right;
                var c = right;
                return new BoundOrQuery(
                    ToDisjunctiveNormalForm(new BoundAndQuery(a, c)),
                    ToDisjunctiveNormalForm(new BoundAndQuery(b, c))
                );
            }

            // A AND (B OR C)      ->    (A AND B) OR (A AND C)

            if (right is BoundOrQuery rightOr)
            {
                var a = left;
                var b = rightOr.Left;
                var c = rightOr.Right;
                return new BoundOrQuery(
                    ToDisjunctiveNormalForm(new BoundAndQuery(a, b)),
                    ToDisjunctiveNormalForm(new BoundAndQuery(a, c))
                );
            }

            return new BoundAndQuery(left, right);
        }

        return node;
    }

    private static BoundQuery Negate(BoundQuery node)
    {
        return node switch
        {
            BoundKeyValueQuery kevValue => NegateKevValueQuery(kevValue),
            BoundTextQuery text => NegateTextQuery(text),
            BoundNegatedQuery negated => NegateNegatedQuery(negated),
            BoundAndQuery and => NegateAndQuery(and),
            BoundOrQuery or => NegateOrQuery(or),
            _ => throw new ArgumentOutOfRangeException(nameof(node), $"Unexpected node {node.GetType()}"),
        };
    }

    private static BoundKeyValueQuery NegateKevValueQuery(BoundKeyValueQuery node)
    {
        return new BoundKeyValueQuery(!node.IsNegated, node.Key, node.Value, node.Operator);
    }

    private static BoundTextQuery NegateTextQuery(BoundTextQuery node)
    {
        return new BoundTextQuery(!node.IsNegated, node.Text);
    }

    private static BoundQuery NegateNegatedQuery(BoundNegatedQuery node)
    {
        return node.Query;
    }

    private static BoundOrQuery NegateAndQuery(BoundAndQuery node)
    {
        return new BoundOrQuery(Negate(node.Left), Negate(node.Right));
    }

    private static BoundAndQuery NegateOrQuery(BoundOrQuery node)
    {
        return new BoundAndQuery(Negate(node.Left), Negate(node.Right));
    }

    private static IReadOnlyList<BoundQuery>[] Flatten(BoundQuery node)
    {
        var disjunctions = new List<IReadOnlyList<BoundQuery>>();
        var conjunctions = new List<BoundQuery>();

        foreach (var or in FlattenOrs(node))
        {
            conjunctions.Clear();

            foreach (var conjunction in FlattenAnds(or))
                conjunctions.Add(conjunction);

            disjunctions.Add(conjunctions.ToArray());
        }

        return disjunctions.ToArray();
    }

    private static List<BoundQuery> FlattenAnds(BoundQuery node)
    {
        var stack = new Stack<BoundQuery>();
        var result = new List<BoundQuery>();
        stack.Push(node);

        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n is not BoundAndQuery and)
            {
                result.Add(n);
            }
            else
            {
                stack.Push(and.Right);
                stack.Push(and.Left);
            }
        }

        return result;
    }

    private static List<BoundQuery> FlattenOrs(BoundQuery node)
    {
        var stack = new Stack<BoundQuery>();
        var result = new List<BoundQuery>();
        stack.Push(node);

        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n is not BoundOrQuery or)
            {
                result.Add(n);
            }
            else
            {
                stack.Push(or.Right);
                stack.Push(or.Left);
            }
        }

        return result;
    }

    public override string ToString()
    {
        using var stringWriter = new StringWriter();
        {
            using var indentedTextWriter = new IndentedTextWriter(stringWriter);
            Walk(indentedTextWriter, this);

            return stringWriter.ToString();
        }

        static void Walk(IndentedTextWriter writer, BoundQuery node)
        {
            switch (node)
            {
                case BoundKeyValueQuery keyValue:
                    writer.WriteLine($"{(keyValue.IsNegated ? "-" : "")}{keyValue.Key}{ToString(keyValue.Operator)}{keyValue.Value}");
                    break;
                case BoundTextQuery text:
                    writer.WriteLine($"{(text.IsNegated ? "-" : "")}{text.Text}");
                    break;
                case BoundNegatedQuery negated:
                    writer.WriteLine("NOT");
                    writer.Indent++;
                    Walk(writer, negated.Query);
                    writer.Indent--;
                    break;
                case BoundAndQuery and:
                    writer.WriteLine("AND");
                    writer.Indent++;
                    Walk(writer, and.Left);
                    Walk(writer, and.Right);
                    writer.Indent--;
                    break;
                case BoundOrQuery or:
                    writer.WriteLine("OR");
                    writer.Indent++;
                    Walk(writer, or.Left);
                    Walk(writer, or.Right);
                    writer.Indent--;
                    break;
                default:
                    writer.WriteLine(node.GetType().FullName);
                    break;
            }

            static string ToString(KeyValueOperator op)
            {
                return op switch
                {
                    KeyValueOperator.EqualTo => "=",
                    KeyValueOperator.NotEqualTo => "<>",
                    KeyValueOperator.LessThan => "<",
                    KeyValueOperator.LessThanOrEqual => "<=",
                    KeyValueOperator.GreaterThan => ">",
                    KeyValueOperator.GreaterThanOrEqual => ">=",
                    _ => op.ToString(),
                };
            }
        }
    }
}
