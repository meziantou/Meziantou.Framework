using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.SimpleQueryLanguage.Binding;
using Meziantou.Framework.SimpleQueryLanguage.Syntax;

namespace Meziantou.Framework.SimpleQueryLanguage.Fuzz;

/// <summary>
/// Parsing a query never fails (except with <see cref="QueryTooComplexException"/>), and the tokens of the syntax tree cover
/// the non-whitespace characters of the query in order. The disjunctive normal form of the bound query is equivalent to
/// the syntax tree. <see cref="QueryBuilder{T}"/> and <see cref="ExpressionQueryBuilder{T}"/> evaluate a query as the
/// AND/OR/NOT combination of its terms, each term evaluated as a query of its own; compiling the expression, interpreting
/// it, and applying it to an <see cref="IQueryable{T}"/> agree.
/// </summary>
public static partial class SimpleQueryLanguageFuzzTargets
{
    // The oracle builds one query per distinct term, and an expression is compiled three times: bound the work
    private const int MaxTerms = 64;
    private const int MaxExpressionTerms = 2048;
    private const int TruthAssignments = 4;

    private const byte FreeTextOption = 1;
    private const byte UnhandledPropertyOption = 2;
    private const byte IgnoreCaseOption = 4;

    private static readonly DateTimeOffset Now = new(2026, 3, 15, 13, 45, 30, TimeSpan.Zero);

    private static readonly QueryItem[] Items =
    [
        new(0, "John Doe", 42, Rank: 1, new DateTime(2026, 3, 15, 10, 0, 0, DateTimeKind.Utc), new DateOnly(2026, 3, 15), 4.5, IsOpen: true, QueryItemStatus.Active, ["bug", "ui"]),
        new(1, Name: null, 0, Rank: null, new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateOnly(2022, 1, 1), -1, IsOpen: false, QueryItemStatus.Closed, []),
        new(2, "jane", -10, Rank: -5, new DateTime(2026, 3, 9, 0, 0, 0, DateTimeKind.Utc), new DateOnly(2026, 2, 28), double.NaN, IsOpen: true, QueryItemStatus.Pending, ["feature"]),
        new(3, "", int.MaxValue, Rank: int.MinValue, DateTime.MaxValue, DateOnly.MaxValue, double.PositiveInfinity, IsOpen: false, (QueryItemStatus)42, ["BUG"]),
        new(4, "\"quoted\" name:value", int.MinValue, Rank: 0, DateTime.MinValue, DateOnly.MinValue, 0, IsOpen: true, QueryItemStatus.Active, ["a", "b", "c"]),
    ];

    private static readonly string[] Tokens =
    [
        "AND", "OR", "NOT", "and", "or", "not", "-", "(", ")", ":", "=", "<", "<=", ">", ">=", "<>", "\"", "\"\"", "..", "*", " ", "\t", "\u00A0", "\u3000",
        "name", "age", "rank", "created", "day", "score", "open", "status", "is", "tag", "other", "today", "yesterday", "this week", "this_week", "this month",
        "last_month", "this year", "last year", "@today", "@today-1w", "@today+3d", "2026-03-15", "2022-01-01T00:00:00Z", "10:00", "true", "false", "Active",
        "Closed", "Pending", "closed", "1", "-1", "0", "1.5", "1e308", "NaN", "Infinity", "2147483647", "2147483648",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("query-parse", Parse) { Seeds = () => GetSeeds(optionsMask: 0xFF), Dictionary = Tokens },
        new("query-builder", Builder) { Seeds = () => GetSeeds(optionsMask: FreeTextOption | UnhandledPropertyOption), Dictionary = Tokens },
        new("query-expression", ExpressionBuilder) { Seeds = () => GetSeeds(optionsMask: FreeTextOption | UnhandledPropertyOption | IgnoreCaseOption), Dictionary = Tokens },
    ];

    /// <summary>
    /// Checks the syntax tree (token positions, kinds and values, node spans), that each term parses back alone, and that
    /// the disjunctive normal form of <see cref="BoundQuery.Create"/> has the truth value of the syntax tree for a few
    /// truth assignments of the terms (the first byte varies the assignments).
    /// </summary>
    private static void Parse(ReadOnlySpan<byte> data)
    {
        var salt = FuzzInput.ReadByte(ref data);
        var text = FuzzText.Decode(data);

        var syntax = ParseQuery(text);
        if (syntax is null)
            return;

        var terms = CheckSyntaxTree(syntax, text);
        FuzzAssert.Equal(syntax.ToString(), QuerySyntax.Parse(text).ToString(), "Parsing the same query twice gives different trees");

        foreach (var term in terms.Take(MaxTerms))
        {
            FuzzAssert.True(IsStandalone(term) || IsContextDependentKey(term.Key), $"The term {FuzzAssert.Format(GetTermText(term))} does not parse back alone");
        }

        IReadOnlyList<IReadOnlyList<BoundQuery>> disjunctions;
        try
        {
            disjunctions = BoundQuery.Create(syntax);
        }
        catch (QueryTooComplexException)
        {
            return;
        }

        FuzzAssert.True(disjunctions.Count > 0, "The disjunctive normal form is empty");
        foreach (var conjunction in disjunctions)
        {
            FuzzAssert.True(conjunction.Count > 0, "A conjunction of the disjunctive normal form is empty");
            foreach (var bound in conjunction)
            {
                FuzzAssert.True(bound is BoundTextQuery or BoundKeyValueQuery, $"The disjunctive normal form holds a {bound.GetType().Name}");
            }
        }

        for (var i = 0; i < TruthAssignments; i++)
        {
            var assignment = (salt * TruthAssignments) + i;
            var expected = Evaluate(syntax, term => IsTrue(term, assignment));
            var actual = disjunctions.Any(conjunction => conjunction.All(bound => bound switch
            {
                BoundTextQuery textQuery => IsTrue(new Term(Key: null, KeyValueOperator.EqualTo, textQuery.Text), assignment) != textQuery.IsNegated,
                BoundKeyValueQuery keyValueQuery => IsTrue(new Term(keyValueQuery.Key, keyValueQuery.Operator, keyValueQuery.Value), assignment) != keyValueQuery.IsNegated,
                _ => throw new FuzzAssertionException($"Unexpected bound query {bound.GetType().Name}"),
            }));

            FuzzAssert.Equal(expected, actual, $"The disjunctive normal form and the syntax tree of {FuzzAssert.Format(text)} disagree (assignment {assignment})");
        }
    }

    /// <summary>
    /// Builds the query with a <see cref="QueryBuilder{T}"/> holding handlers of several kinds (the first byte toggles the free
    /// text and unhandled property handlers), and evaluates it on sample items: the result is the AND/OR/NOT combination,
    /// following the syntax tree, of each term built as a query of its own.
    /// </summary>
    private static void Builder(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var text = FuzzText.Decode(data);
        var builder = CreateQueryBuilder(options);

        Query<QueryItem> query;
        try
        {
            query = builder.Build(text);
        }
        catch (QueryTooComplexException)
        {
            return;
        }

        FuzzAssert.Equal(text, query.Text, "Query.Text is not the query");
        var oracle = CreateOracle(text, term => builder.Build(GetTermText(term)).Evaluate);
        foreach (var item in Items)
        {
            var actual = query.Evaluate(item);
            if (oracle is not null)
            {
                FuzzAssert.Equal(oracle(item), actual, $"{FuzzAssert.Format(text)} and the combination of its terms disagree on item {item.Id}");
            }
        }
    }

    /// <summary>
    /// Builds the query with an <see cref="ExpressionQueryBuilder{T}"/> (the first byte toggles the free text and unhandled
    /// property handlers, and the case sensitivity of a string handler). The compiled expression, the interpreted expression
    /// and <see cref="ExpressionQuery{T}.Apply"/> agree, and match the AND/OR/NOT combination of the terms built alone.
    /// </summary>
    private static void ExpressionBuilder(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var text = FuzzText.Decode(data);
        var builder = CreateExpressionQueryBuilder(options);

        ExpressionQuery<QueryItem> query;
        try
        {
            query = builder.Build(text);
        }
        catch (QueryTooComplexException)
        {
            return;
        }

        FuzzAssert.Equal(text, query.Text, "ExpressionQuery.Text is not the query");
        FuzzAssert.Equal(string.IsNullOrWhiteSpace(text), query.Predicate is null, "The predicate is null for a query that is not empty, or the reverse");
        if (query.Predicate is null)
        {
            FuzzAssert.Equal(Items.Length, query.Apply(Items.AsQueryable()).Count(), "An empty query filters items");
            return;
        }

        // Compiling a large expression is slow; the size of its disjunctive normal form bounds it
        if (BoundQuery.Create(QuerySyntax.Parse(text)).Sum(conjunction => conjunction.Count) > MaxExpressionTerms)
            return;

        var compiled = query.Predicate.Compile();
        var interpreted = query.Predicate.Compile(preferInterpretation: true);
        var applied = query.Apply(Items.AsQueryable()).ToHashSet();
        var oracle = CreateOracle(text, term => builder.Build(GetTermText(term)).Predicate!.Compile(preferInterpretation: true));
        foreach (var item in Items)
        {
            var actual = compiled(item);
            FuzzAssert.Equal(actual, interpreted(item), $"The compiled and interpreted expressions of {FuzzAssert.Format(text)} disagree on item {item.Id}");
            FuzzAssert.Equal(actual, applied.Contains(item), $"The compiled expression and Apply of {FuzzAssert.Format(text)} disagree on item {item.Id}");
            if (oracle is not null)
            {
                FuzzAssert.Equal(oracle(item), actual, $"{FuzzAssert.Format(text)} and the combination of its terms disagree on item {item.Id}");
            }
        }
    }

    private static QueryBuilder<QueryItem> CreateQueryBuilder(byte options)
    {
        var builder = new QueryBuilder<QueryItem>(new FixedTimeProvider(Now));
        builder.AddHandler<string>("name", (item, value) => item.Name is not null && item.Name.Contains(value, StringComparison.Ordinal));
        builder.AddRangeHandler<int>("age", (item, range) => range.IsInRange(item.Age));
        builder.AddRangeHandler<int?>("rank", (item, range) => range.IsInRange(item.Rank));
        builder.AddRangeHandler<DateTime>("created", (item, range) => range.IsInRange(item.Created));
        builder.AddRangeHandler<DateOnly>("day", (item, range) => range.IsInRange(item.Day));
        builder.AddRangeHandler<double>("score", (item, range) => range.IsInRange(item.Score));
        builder.AddHandler<bool>("open", (item, value) => item.IsOpen == value);
        builder.AddHandler<QueryItemStatus>("status", (item, op, value) => op switch
        {
            KeyValueOperator.EqualTo => item.Status == value,
            KeyValueOperator.NotEqualTo => item.Status != value,
            _ => item.Status.CompareTo(value) < 0,
        });
        builder.AddHandler("is", "open", item => item.IsOpen);
        builder.AddHandler("is", "closed", item => !item.IsOpen);
        builder.AddHandler("tag", (QueryItem item, KeyValueOperator op, string value) => op is KeyValueOperator.NotEqualTo ? !item.Tags.Contains(value) : item.Tags.Contains(value));
        if ((options & FreeTextOption) != 0)
        {
            builder.SetTextFilterHandler(MatchesText);
        }

        if ((options & UnhandledPropertyOption) != 0)
        {
            builder.SetUnhandledPropertyHandler(MatchesProperty);
        }

        return builder;
    }

    private static ExpressionQueryBuilder<QueryItem> CreateExpressionQueryBuilder(byte options)
    {
        var builder = new ExpressionQueryBuilder<QueryItem>(new FixedTimeProvider(Now));
        builder.AddHandler("name", item => item.Name, (options & IgnoreCaseOption) != 0 ? StringComparison.OrdinalIgnoreCase : null);
        builder.AddHandler<int>("age", item => item.Age);
        builder.AddHandler<int?>("rank", item => item.Rank);
        builder.AddHandler<DateTime>("created", item => item.Created);
        builder.AddHandler<DateOnly>("day", item => item.Day);
        builder.AddHandler<double>("score", item => item.Score);
        builder.AddHandler<bool>("open", item => item.IsOpen);
        builder.AddHandler<QueryItemStatus>("status", item => item.Status);
        builder.AddHandler("tag", KeyValueOperator.EqualTo, value => item => item.Tags.Contains(value));
        builder.AddHandler("tag", KeyValueOperator.LessThan, value => item => item.Tags.Count < value.Length);
        if ((options & FreeTextOption) != 0)
        {
            builder.SetFreeTextHandler(text => item => MatchesText(item, text));
        }

        if ((options & UnhandledPropertyOption) != 0)
        {
            // Returning null for some properties falls back to free text
            builder.SetUnhandledPropertyHandler((name, op, value) => Hash(name) % 3 == 0 ? null : item => MatchesProperty(item, name, op, value));
        }

        return builder;
    }

    private static bool MatchesText(QueryItem item, string text)
    {
        return (item.Name is not null && text.Length > 0 && item.Name.Contains(text, StringComparison.Ordinal)) || (unchecked(Hash(text) + (uint)item.Id) % 3 == 0);
    }

    private static bool MatchesProperty(QueryItem item, string name, KeyValueOperator op, string value)
    {
        return unchecked(Hash(name + (char)op + value) + (uint)item.Id) % 2 == 0;
    }

    /// <summary>
    /// Evaluates the query as the combination of its terms that its syntax tree describes, each term evaluated by
    /// <paramref name="createTerm"/>. Returns <see langword="null"/> when the query has too many terms.
    /// </summary>
    private static Func<QueryItem, bool>? CreateOracle(string text, Func<Term, Func<QueryItem, bool>> createTerm)
    {
        if (string.IsNullOrWhiteSpace(text))
            return _ => true;

        var syntax = QuerySyntax.Parse(text);
        var terms = GetTerms(syntax);
        if (terms.Count > MaxTerms || !terms.All(IsStandalone))
            return null;

        var predicates = new Dictionary<Term, Func<QueryItem, bool>>();
        foreach (var term in terms)
        {
            predicates.Add(term, createTerm(term));
        }

        return item => Evaluate(syntax, term => predicates[term](item));
    }

    // Walks the tree with an explicit stack: a long conjunction is a left-leaning tree as deep as its term count
    private static bool Evaluate(QuerySyntax root, Func<Term, bool> evaluateTerm)
    {
        var stack = new Stack<(QuerySyntax Node, bool Visited)>();
        var values = new Stack<bool>();
        stack.Push((root, false));
        while (stack.TryPop(out var item))
        {
            switch (item.Node)
            {
                case TextQuerySyntax node:
                    values.Push(evaluateTerm(GetTerm(node)));
                    break;

                case KeyValueQuerySyntax node:
                    values.Push(evaluateTerm(GetTerm(node)));
                    break;

                case ParenthesizedQuerySyntax node:
                    stack.Push((node.Query, false));
                    break;

                case NegatedQuerySyntax node when !item.Visited:
                    stack.Push((node, true));
                    stack.Push((node.Query, false));
                    break;

                case NegatedQuerySyntax:
                    values.Push(!values.Pop());
                    break;

                case AndQuerySyntax node when !item.Visited:
                    stack.Push((node, true));
                    stack.Push((node.Right, false));
                    stack.Push((node.Left, false));
                    break;

                case OrQuerySyntax node when !item.Visited:
                    stack.Push((node, true));
                    stack.Push((node.Right, false));
                    stack.Push((node.Left, false));
                    break;

                case AndQuerySyntax:
                {
                    var right = values.Pop();
                    var left = values.Pop();
                    values.Push(left && right);
                    break;
                }

                case OrQuerySyntax:
                {
                    var right = values.Pop();
                    var left = values.Pop();
                    values.Push(left || right);
                    break;
                }

                default:
                    throw new FuzzAssertionException($"Unexpected node {item.Node.Kind}");
            }
        }

        FuzzAssert.Equal(1, values.Count, "The syntax tree does not evaluate to a single value");
        return values.Pop();
    }

    /// <summary>Checks the tokens and nodes of the tree, and returns the distinct terms of the query.</summary>
    private static HashSet<Term> CheckSyntaxTree(QuerySyntax root, string text)
    {
        var terms = new HashSet<Term>();
        var previousEnd = 0;
        var stack = new Stack<QueryNodeOrToken>();
        stack.Push(root);
        while (stack.TryPop(out var current))
        {
            var span = current.Span;
            FuzzAssert.True(span.Start >= 0 && span.Start <= span.End && span.End <= text.Length, $"The span {span} of a {current.Kind} is outside the query of length {text.Length}");

            if (current is QueryToken token)
            {
                CheckToken(token, text, ref previousEnd);
                continue;
            }

            var node = (QuerySyntax)current;
            var children = node.GetChildren();
            FuzzAssert.True(children.Length > 0, $"The {node.Kind} has no child");
            FuzzAssert.Equal(children[0].Span.Start, span.Start, $"The span of a {node.Kind} does not start at its first child");
            FuzzAssert.Equal(children[^1].Span.End, span.End, $"The span of a {node.Kind} does not end at its last child");
            CheckNode(node, text, terms);
            for (var i = children.Length - 1; i >= 0; i--)
            {
                stack.Push(children[i]);
            }
        }

        FuzzAssert.True(text.AsSpan(previousEnd).IsWhiteSpace(), $"The tokens do not cover the end of the query from {previousEnd}");
        return terms;
    }

    private static void CheckNode(QuerySyntax node, string text, HashSet<Term> terms)
    {
        switch (node)
        {
            case TextQuerySyntax textQuery:
                FuzzAssert.True(textQuery.TextToken.Kind is QuerySyntaxKind.TextToken or QuerySyntaxKind.QuotedTextToken, $"The text of a query is a {textQuery.TextToken.Kind}");
                FuzzAssert.True(textQuery.TextToken.Span.Length > 0 || text.AsSpan().IsWhiteSpace(), "A text query is empty");
                terms.Add(GetTerm(textQuery));
                break;

            case KeyValueQuerySyntax keyValue:
                FuzzAssert.Equal(QuerySyntaxKind.TextToken, keyValue.KeyToken.Kind, "The key of a key-value query is not text");
                FuzzAssert.True(keyValue.KeyToken.Span.Length > 0, "The key of a key-value query is empty");
                FuzzAssert.Equal(keyValue.KeyToken.Span.End, keyValue.OperatorToken.Span.Start, "The operator of a key-value query does not follow its key");
                FuzzAssert.Equal(keyValue.OperatorToken.Span.End, keyValue.ValueToken.Span.Start, "The value of a key-value query does not follow its operator");
                FuzzAssert.True(keyValue.ValueToken.Kind is QuerySyntaxKind.TextToken or QuerySyntaxKind.QuotedTextToken or QuerySyntaxKind.TextQuery, $"The value of a key-value query is a {keyValue.ValueToken.Kind}");
                terms.Add(GetTerm(keyValue));
                break;

            case AndQuerySyntax andQuery:
                FuzzAssert.Equal(andQuery.Operator is null, andQuery.IsImplicit, "IsImplicit disagrees with Operator");
                FuzzAssert.True(andQuery.Operator is null or { Kind: QuerySyntaxKind.AndKeyword }, "The operator of an AND query is not AND");
                break;

            case OrQuerySyntax orQuery:
                FuzzAssert.Equal(QuerySyntaxKind.OrKeyword, orQuery.OperatorToken.Kind, "The operator of an OR query is not OR");
                break;

            case NegatedQuerySyntax negatedQuery:
                FuzzAssert.Equal(QuerySyntaxKind.NotKeyword, negatedQuery.OperatorToken.Kind, "The operator of a NOT query is not NOT");
                break;

            case ParenthesizedQuerySyntax parenthesizedQuery:
                FuzzAssert.Equal(QuerySyntaxKind.OpenParenthesisToken, parenthesizedQuery.OpenParenthesisToken.Kind, "A parenthesized query does not start with '('");
                FuzzAssert.Equal(QuerySyntaxKind.CloseParenthesisToken, parenthesizedQuery.CloseParenthesisToken.Kind, "A parenthesized query does not end with ')'");
                break;

            default:
                FuzzAssert.Fail($"Unexpected node {node.Kind}");
                break;
        }
    }

    private static void CheckToken(QueryToken token, string text, ref int previousEnd)
    {
        var span = token.Span;
        FuzzAssert.True(ReferenceEquals(text, token.QueryText), "The token does not hold the query text");
        FuzzAssert.True(span.Start >= previousEnd, $"The token {span} overlaps or precedes the previous token, which ends at {previousEnd}");
        FuzzAssert.True(text.AsSpan(previousEnd, span.Start - previousEnd).IsWhiteSpace(), $"The text between {previousEnd} and the token {span} is not whitespace");
        previousEnd = span.End;

        var tokenText = token.Text;
        FuzzAssert.Equal(text.Substring(span.Start, span.Length), tokenText, "The text of a token is not the text of its span");

        // A missing token (the closing parenthesis of "(a", the empty value of "key:") is empty
        if (span.Length == 0)
        {
            FuzzAssert.True(token.Value is null or "", "An empty token has a value");
            FuzzAssert.True(token.Kind is QuerySyntaxKind.TextToken or QuerySyntaxKind.TextQuery || span.Start == text.Length, $"An empty {token.Kind} is not at the end of the query");
            return;
        }

        switch (token.Kind)
        {
            case QuerySyntaxKind.TextToken:
            case QuerySyntaxKind.TextQuery:
                FuzzAssert.Equal(tokenText, token.Value, $"The value of a {token.Kind} is not its text");
                break;

            case QuerySyntaxKind.QuotedTextToken:
                FuzzAssert.True(token.Value is not null, "A quoted text has no value");
                var escaped = "\"" + token.Value.Replace("\"", "\"\"", StringComparison.Ordinal);
                if (tokenText != escaped + "\"")
                {
                    FuzzAssert.Equal(escaped, tokenText, "The value of a quoted text is not its unescaped text");
                    FuzzAssert.Equal(text.Length, span.End, "A quoted text without its closing quote does not end the query");
                }

                break;

            default:
                var expected = token.Kind switch
                {
                    QuerySyntaxKind.OpenParenthesisToken => "(",
                    QuerySyntaxKind.CloseParenthesisToken => ")",
                    QuerySyntaxKind.ColonToken => ":",
                    QuerySyntaxKind.EqualOperatorToken => "=",
                    QuerySyntaxKind.NotEqualOperatorToken => "<>",
                    QuerySyntaxKind.LessThanOperatorToken => "<",
                    QuerySyntaxKind.LessThanOrEqualOperatorToken => "<=",
                    QuerySyntaxKind.GreaterThanOperatorToken => ">",
                    QuerySyntaxKind.GreaterThanOrEqualOperatorToken => ">=",
                    QuerySyntaxKind.AndKeyword => "and",
                    QuerySyntaxKind.OrKeyword => "or",
                    QuerySyntaxKind.NotKeyword => tokenText is "-" ? "-" : "not",
                    _ => throw new FuzzAssertionException($"Unexpected token {token.Kind}"),
                };

                FuzzAssert.True(string.Equals(expected, tokenText, StringComparison.OrdinalIgnoreCase), $"The text {FuzzAssert.Format(tokenText)} of a {token.Kind} is not {FuzzAssert.Format(expected)}");
                break;
        }
    }

    private static HashSet<Term> GetTerms(QuerySyntax root)
    {
        var terms = new HashSet<Term>();
        var stack = new Stack<QuerySyntax>();
        stack.Push(root);
        while (stack.TryPop(out var node))
        {
            switch (node)
            {
                case TextQuerySyntax textQuery:
                    terms.Add(GetTerm(textQuery));
                    break;

                case KeyValueQuerySyntax keyValue:
                    terms.Add(GetTerm(keyValue));
                    break;

                default:
                    foreach (var child in node.GetChildren())
                    {
                        if (child is QuerySyntax childNode)
                        {
                            stack.Push(childNode);
                        }
                    }

                    break;
            }
        }

        return terms;
    }

    private static Term GetTerm(TextQuerySyntax node)
    {
        FuzzAssert.True(node.TextToken.Value is not null, "A text query has no value");
        return new Term(Key: null, KeyValueOperator.EqualTo, node.TextToken.Value);
    }

    private static Term GetTerm(KeyValueQuerySyntax node)
    {
        FuzzAssert.True(node.KeyToken.Value is not null && node.ValueToken.Value is not null, "A key-value query has no key or no value");
        var op = node.OperatorToken.Kind switch
        {
            QuerySyntaxKind.ColonToken or QuerySyntaxKind.EqualOperatorToken => KeyValueOperator.EqualTo,
            QuerySyntaxKind.NotEqualOperatorToken => KeyValueOperator.NotEqualTo,
            QuerySyntaxKind.LessThanOperatorToken => KeyValueOperator.LessThan,
            QuerySyntaxKind.LessThanOrEqualOperatorToken => KeyValueOperator.LessThanOrEqual,
            QuerySyntaxKind.GreaterThanOperatorToken => KeyValueOperator.GreaterThan,
            QuerySyntaxKind.GreaterThanOrEqualOperatorToken => KeyValueOperator.GreaterThanOrEqual,
            _ => throw new FuzzAssertionException($"The operator of a key-value query is a {node.OperatorToken.Kind}"),
        };

        return new Term(node.KeyToken.Value, op, node.ValueToken.Value);
    }

    // Whether the query made of the term alone is that term
    private static bool IsStandalone(Term term)
    {
        return ParseQuery(GetTermText(term)) switch
        {
            TextQuerySyntax textQuery => GetTerm(textQuery) == term,
            KeyValueQuerySyntax keyValueQuery => GetTerm(keyValueQuery) == term,
            _ => false,
        };
    }

    /// <summary>
    /// Gets whether a key is a parenthesis or an operator. The parser reads it as text, and so as a key, only after another
    /// term: "a ):b" and "a ::b" hold the key-value queries "):b" and "::b", but alone, "):b" and "::b" are text queries.
    /// </summary>
    private static bool IsContextDependentKey(string? key) => key is ")" or ":" or "=" or "<" or ">" or "<=" or ">=" or "<>";

    // The query made of the term alone, with the value quoted
    private static string GetTermText(Term term)
    {
        var value = "\"" + term.Value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        if (term.Key is null)
            return value;

        var op = term.Operator switch
        {
            KeyValueOperator.EqualTo => ":",
            KeyValueOperator.NotEqualTo => "<>",
            KeyValueOperator.LessThan => "<",
            KeyValueOperator.LessThanOrEqual => "<=",
            KeyValueOperator.GreaterThan => ">",
            KeyValueOperator.GreaterThanOrEqual => ">=",
            _ => throw new FuzzAssertionException($"Unexpected operator {term.Operator}"),
        };

        return term.Key + op + value;
    }

    private static QuerySyntax? ParseQuery(string text)
    {
        try
        {
            return QuerySyntax.Parse(text);
        }
        catch (QueryTooComplexException)
        {
            return null;
        }
    }

    private static bool IsTrue(Term term, int assignment)
    {
        return unchecked((Hash(term.Key ?? "\0") * 31) + (Hash(term.Value) * 7) + (uint)term.Operator + (uint)assignment) % 5 < 2 ^ (assignment % 2 == 0);
    }

    // FNV-1a: string.GetHashCode is randomized per process, and a failure must replay
    private static uint Hash(string value)
    {
        var hash = 2166136261u;
        foreach (var c in value)
        {
            hash = unchecked((hash ^ c) * 16777619u);
        }

        return hash;
    }

    private static IEnumerable<FuzzSeed> GetSeeds(byte optionsMask)
    {
        var index = 0;
        foreach (var text in GetTestStrings())
        {
            var options = (byte)(index & optionsMask);
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"{index:D3}-{text}"), [options], text);
            index++;
        }
    }

    // The string literals of the unit tests: queries, values, and a few other strings
    private static IEnumerable<string> GetTestStrings()
    {
        string[] queries =
        [
            "name:john AND age>=21", "(name:\"john doe\" OR name:jane) AND age>21", "name<>john", "-name:john", "NOT name:john", "created:\"this week\"", "created:this_week",
            "age:13..19", "stars:10..*", "created:*..2024-12-31", "created>@today-1w", "created:@today-2w..@today", "is_open:true free form text",
            "is_open:true AND NOT \"free form text\"", "status:Active OR (rank<=0 -tag:bug) day>=2026-01-01 score:1.5..*", "is:open tag<abc other:value \"a \"\" b\"",
        ];

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var query in queries)
        {
            if (result.Add(query))
                yield return query;
        }

        foreach (var name in (string[])["QueryBuilderTests.cs", "ExpressionQueryBuilderTests.cs", "ValueParserTests.cs"])
        {
            var source = FuzzResources.ReadText(typeof(SimpleQueryLanguageFuzzTargets).Assembly, name);
            foreach (Match match in StringLiteralRegex().Matches(source))
            {
                var value = Unescape(match.Groups["value"].Value);
                if (value.Length > 0 && result.Add(value))
                    yield return value;
            }
        }
    }

    private static string Unescape(string value)
    {
        var result = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                result.Append(value[i]);
                continue;
            }

            i++;
            switch (value[i])
            {
                case 'n':
                    result.Append('\n');
                    break;
                case 'r':
                    result.Append('\r');
                    break;
                case 't':
                    result.Append('\t');
                    break;
                case '0':
                    result.Append('\0');
                    break;
                case 'u' when i + 4 < value.Length:
                    result.Append((char)int.Parse(value.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                default:
                    result.Append(value[i]);
                    break;
            }
        }

        return result.ToString();
    }

    [GeneratedRegex(@"(?<![@$])""(?<value>(?:[^""\\\r\n]|\\.)*)""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex StringLiteralRegex();

    /// <summary>A term of a query: free text when <see cref="Key"/> is <see langword="null"/>, otherwise a key-value query.</summary>
    private readonly record struct Term(string? Key, KeyValueOperator Operator, string Value);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private enum QueryItemStatus
    {
        Active,
        Closed,
        Pending,
    }

    private sealed record QueryItem(int Id, string? Name, int Age, int? Rank, DateTime Created, DateOnly Day, double Score, bool IsOpen, QueryItemStatus Status, IReadOnlyList<string> Tags);
}
