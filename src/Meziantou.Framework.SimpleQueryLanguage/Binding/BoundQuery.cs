using System.CodeDom.Compiler;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Meziantou.Framework.SimpleQueryLanguage.Syntax;

namespace Meziantou.Framework.SimpleQueryLanguage.Binding;

/// <summary>Represents a bound query node in the query tree after semantic analysis.</summary>
public abstract class BoundQuery
{
    /// <summary>
    /// The largest number of disjunctions a query may expand to. Converting to disjunctive normal form
    /// distributes AND over OR, so the term count grows exponentially with the number of OR groups.
    /// </summary>
    private const int MaxDisjunctions = 8192;

    /// <summary>
    /// The largest number of terms, summed over all disjunctions, a query may expand to. Distributing copies
    /// the terms combined with an OR group into every disjunction, so a few OR groups next to a long list of
    /// terms stay under <see cref="MaxDisjunctions"/> while still producing millions of terms.
    /// </summary>
    private const int MaxTerms = 1 << 18;

    /// <summary>Creates a disjunctive normal form representation of the query syntax tree.</summary>
    /// <param name="syntax">The query syntax tree to bind.</param>
    /// <returns>A list of disjunctions, where each disjunction is a list of conjunctions.</returns>
    /// <exception cref="QueryTooComplexException">The query nests expressions too deeply, or expands to too many disjunctions or terms.</exception>
    public static IReadOnlyList<IReadOnlyList<BoundQuery>> Create(QuerySyntax syntax)
    {
        ArgumentNullException.ThrowIfNull(syntax);

        try
        {
            var result = CreateInternal(syntax);
            EnsureDisjunctiveNormalFormSizeIsSupported(result);

            return ToDisjunctiveNormalForm(result, isNegated: false);
        }
        catch (InsufficientExecutionStackException ex)
        {
            throw new QueryTooComplexException("The query nests expressions too deeply to be evaluated", ex);
        }
    }

    /// <summary>
    /// Computes the size of the disjunctive normal form without materializing it, so an oversized query
    /// is rejected before any work is done.
    /// </summary>
    private static void EnsureDisjunctiveNormalFormSizeIsSupported(BoundQuery node)
    {
        var (disjunctions, terms) = MeasureDisjunctiveNormalForm(node, isNegated: false);
        if (disjunctions > MaxDisjunctions)
            throw new QueryTooComplexException($"The query expands to more than {MaxDisjunctions} disjunctions. Reduce the number of OR groups combined with AND.");

        if (terms > MaxTerms)
            throw new QueryTooComplexException($"The query expands to more than {MaxTerms} terms. Reduce the number of terms combined with OR groups.");
    }

    private static (int Disjunctions, int Terms) MeasureDisjunctiveNormalForm(BoundQuery node, bool isNegated)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        if (node is BoundNegatedQuery negatedQuery)
            return MeasureDisjunctiveNormalForm(negatedQuery.Query, !isNegated);

        if (!TryGetOperands(node, isNegated, out var left, out var right, out var isAnd))
            return (1, 1);

        var (leftDisjunctions, leftTerms) = MeasureDisjunctiveNormalForm(left, isNegated);
        var (rightDisjunctions, rightTerms) = MeasureDisjunctiveNormalForm(right, isNegated);

        // Saturating arithmetic: the exact sizes can overflow an int, and anything past the limit is equally rejected.
        // AND pairs every left disjunction with every right one, so each side's terms are repeated once per disjunction of the other side.
        if (isAnd)
        {
            return (
                Saturate((long)leftDisjunctions * rightDisjunctions, MaxDisjunctions),
                Saturate(((long)leftTerms * rightDisjunctions) + ((long)rightTerms * leftDisjunctions), MaxTerms));
        }

        return (
            Saturate((long)leftDisjunctions + rightDisjunctions, MaxDisjunctions),
            Saturate((long)leftTerms + rightTerms, MaxTerms));

        static int Saturate(long value, int max) => (int)Math.Min(value, max + 1L);
    }

    private static BoundQuery CreateInternal(QuerySyntax syntax)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

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

    /// <summary>Gets the operands of an AND or OR node, taking into account that negation swaps AND and OR.</summary>
    private static bool TryGetOperands(BoundQuery node, bool isNegated, [NotNullWhen(true)] out BoundQuery? left, [NotNullWhen(true)] out BoundQuery? right, out bool isAnd)
    {
        switch (node)
        {
            case BoundAndQuery andQuery:
                (left, right, isAnd) = (andQuery.Left, andQuery.Right, !isNegated);
                return true;

            case BoundOrQuery orQuery:
                (left, right, isAnd) = (orQuery.Left, orQuery.Right, isNegated);
                return true;

            default:
                (left, right, isAnd) = (null, null, false);
                return false;
        }
    }

    private static BoundQuery[][] ToDisjunctiveNormalForm(BoundQuery node, bool isNegated)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        while (node is BoundNegatedQuery negatedQuery)
        {
            node = negatedQuery.Query;
            isNegated = !isNegated;
        }

        if (!TryGetOperands(node, isNegated, out _, out _, out var isAnd))
            return [[isNegated ? NegateLeaf(node) : node]];

        // Collect the operands of the whole AND (or OR) chain before combining them. Distributing one binary node
        // at a time would copy the conjunctions built so far again at every level of a long chain.
        var operands = new List<BoundQuery[][]>();
        var stack = new Stack<(BoundQuery Node, bool IsNegated)>();
        stack.Push((node, isNegated));
        while (stack.TryPop(out var item))
        {
            var (current, currentIsNegated) = item;
            while (current is BoundNegatedQuery negatedQuery)
            {
                current = negatedQuery.Query;
                currentIsNegated = !currentIsNegated;
            }

            if (TryGetOperands(current, currentIsNegated, out var left, out var right, out var currentIsAnd) && currentIsAnd == isAnd)
            {
                stack.Push((right, currentIsNegated));
                stack.Push((left, currentIsNegated));
            }
            else
            {
                operands.Add(ToDisjunctiveNormalForm(current, currentIsNegated));
            }
        }

        return isAnd ? Distribute(operands) : [.. operands.SelectMany(operand => operand)];
    }

    /// <summary>
    /// Combines every disjunction of each operand with every disjunction of the others:
    /// (A OR B) AND C AND (D OR E) -> (A AND C AND D) OR (A AND C AND E) OR (B AND C AND D) OR (B AND C AND E).
    /// </summary>
    private static BoundQuery[][] Distribute(List<BoundQuery[][]> operands)
    {
        // The sizes were validated by EnsureDisjunctiveNormalFormSizeIsSupported, so this cannot overflow
        var count = 1;
        foreach (var operand in operands)
        {
            count *= operand.Length;
        }

        var result = new BoundQuery[count][];
        var indices = new int[operands.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var length = 0;
            for (var j = 0; j < operands.Count; j++)
            {
                length += operands[j][indices[j]].Length;
            }

            var conjunction = new BoundQuery[length];
            var offset = 0;
            for (var j = 0; j < operands.Count; j++)
            {
                var terms = operands[j][indices[j]];
                terms.CopyTo(conjunction, offset);
                offset += terms.Length;
            }

            result[i] = conjunction;

            // Advance to the next combination, the last operand changing fastest
            for (var j = operands.Count - 1; j >= 0; j--)
            {
                indices[j]++;
                if (indices[j] < operands[j].Length)
                    break;

                indices[j] = 0;
            }
        }

        return result;
    }

    private static BoundQuery NegateLeaf(BoundQuery node)
    {
        return node switch
        {
            BoundKeyValueQuery keyValue => new BoundKeyValueQuery(!keyValue.IsNegated, keyValue.Key, keyValue.Value, keyValue.Operator),
            BoundTextQuery text => new BoundTextQuery(!text.IsNegated, text.Text),
            _ => throw new ArgumentOutOfRangeException(nameof(node), $"Unexpected node {node.GetType()}"),
        };
    }

    public override string ToString()
    {
        using var stringWriter = new StringWriter();
        using (var writer = new IndentedTextWriter(stringWriter))
        {
            // Walk with an explicit stack: a long conjunction is a left-leaning tree as deep as its term count
            var stack = new Stack<(BoundQuery Node, int Indent)>();
            stack.Push((this, 0));

            while (stack.TryPop(out var item))
            {
                writer.Indent = item.Indent;
                switch (item.Node)
                {
                    case BoundKeyValueQuery keyValue:
                        writer.WriteLine($"{(keyValue.IsNegated ? "-" : "")}{keyValue.Key}{ToString(keyValue.Operator)}{keyValue.Value}");
                        break;
                    case BoundTextQuery text:
                        writer.WriteLine($"{(text.IsNegated ? "-" : "")}{text.Text}");
                        break;
                    case BoundNegatedQuery negated:
                        writer.WriteLine("NOT");
                        stack.Push((negated.Query, item.Indent + 1));
                        break;
                    case BoundAndQuery and:
                        writer.WriteLine("AND");
                        stack.Push((and.Right, item.Indent + 1));
                        stack.Push((and.Left, item.Indent + 1));
                        break;
                    case BoundOrQuery or:
                        writer.WriteLine("OR");
                        stack.Push((or.Right, item.Indent + 1));
                        stack.Push((or.Left, item.Indent + 1));
                        break;
                    default:
                        writer.WriteLine(item.Node.GetType().FullName);
                        break;
                }
            }
        }

        return stringWriter.ToString();

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
