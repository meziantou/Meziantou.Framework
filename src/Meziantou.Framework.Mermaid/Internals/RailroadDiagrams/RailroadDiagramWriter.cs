using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Mermaid.Internals.RailroadDiagrams;

/// <summary>
/// Writes a <see cref="RailroadDiagram"/> as Mermaid, in its <see cref="RailroadDiagram.Notation"/>. A sequence or a
/// choice of one expression is written as the expression, and a repetition of zero or one time as an optional part, as
/// the parser reads them back.
/// </summary>
internal static class RailroadDiagramWriter
{
    private const int MaxDepth = 256;

    public static void Write(StringBuilder builder, RailroadDiagram diagram)
    {
        var configuration = new List<string>();
        if (diagram.Padding != 8)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        // The title is a statement, as in the examples of Mermaid; the front matter holds the ones a statement cannot
        var titleStatement = FormatTitle(diagram.Title, diagram.Notation);
        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "railroad", configuration, includeTitle: titleStatement is null);
        builder.Append(RailroadDiagramParser.GetKeyword(diagram.Notation)).Append('\n');
        if (titleStatement is not null)
        {
            builder.Append("title ").Append(titleStatement).Append('\n');
        }

        MermaidWriterHelpers.WriteAccessibility(builder, diagram, 0);
        foreach (var rule in diagram.Rules)
        {
            if (!RailroadDiagramParser.IsValidIdentifier(rule.Name, diagram.Notation) || (diagram.Notation is RailroadNotation.Primitives && IsPrimitiveKeyword(rule.Name)))
                throw new ArgumentException($"The name of the rule '{rule.Name}' cannot be written in the notation {diagram.Notation}.", nameof(diagram));

            var definition = diagram.Notation switch
            {
                RailroadNotation.Ebnf => WriteEbnfChoice(rule.Definition, 0),
                RailroadNotation.Abnf => WriteAbnfAlternation(rule.Definition, 0),
                RailroadNotation.Peg => WritePegChoice(rule.Definition, 0),
                _ => WritePrimitive(rule.Definition, 0),
            };
            builder.Append(rule.Name).Append(diagram.Notation is RailroadNotation.Peg ? " <- " : " = ").Append(definition).Append(" ;\n");
        }
    }

    /// <summary>Returns the text of a title statement, which the parser reads back as the title, or <see langword="null"/>.</summary>
    private static string? FormatTitle(string? title, RailroadNotation notation)
    {
        if (title is null || title.Trim().Length is 0 || title.AsSpan().IndexOfAny("\r\n\u2028\u2029\u0085") >= 0 || title.Contains("%%", StringComparison.Ordinal))
            return null;

        // The statement is trimmed and unquoted: such a title is quoted, with escapes but in ABNF
        var quoted = title.Length >= 2 && ((title[0] == '"' && title[^1] == '"') || (title[0] == '\'' && title[^1] == '\''));
        if (!quoted && title.Trim().Length == title.Length)
            return title;

        if (notation is not RailroadNotation.Abnf)
            return Quote(title);

        if (!title.Contains('"', StringComparison.Ordinal))
            return "\"" + title + "\"";

        return title.Contains('\'', StringComparison.Ordinal) ? null : "'" + title + "'";
    }

    private static bool IsPrimitiveKeyword(string name) => name is "sequence" or "choice" or "optional" or "oneOrMore" or "zeroOrMore" or "terminal" or "nonterminal" or "special";

    /// <summary>Returns the expression a sequence or a choice of one item stands for, and the other expressions as they are.</summary>
    private static RailroadExpression Simplify(RailroadExpression expression, int depth)
    {
        for (var i = depth; i <= MaxDepth; i++)
        {
            switch (expression)
            {
                case RailroadSequence { Items.Count: 1 } sequence:
                    expression = sequence.Items[0];
                    continue;
                case RailroadChoice { Alternatives.Count: 1 } choice:
                    expression = choice.Alternatives[0];
                    continue;
                case RailroadSequence { Items.Count: 0 } or RailroadChoice { Alternatives.Count: 0 }:
                    throw new ArgumentException("An empty sequence or choice cannot be written as Mermaid.", nameof(expression));
                case RailroadRepetition { Minimum: 0, Maximum: 1 } repetition:
                    return new RailroadOptional(repetition.Expression);
                default:
                    return expression;
            }
        }

        throw new ArgumentException("The expressions are nested too deeply.", nameof(expression));
    }

    private static void CheckDepth(int depth)
    {
        if (depth > MaxDepth)
            throw new ArgumentException("The expressions are nested too deeply.", nameof(depth));
    }

    private static ArgumentException CannotWrite(RailroadExpression expression, string notation) => new($"The expression {Describe(expression)} cannot be written in {notation}.", nameof(expression));

    private static string Describe(RailroadExpression expression) => expression switch
    {
        RailroadRepetition repetition => $"repeated {repetition.Minimum.ToString(CultureInfo.InvariantCulture)} to {repetition.Maximum?.ToString(CultureInfo.InvariantCulture) ?? "unbounded"} times",
        RailroadSpecial special => $"special '{special.Text}'",
        RailroadTerminal terminal => $"terminal '{terminal.Value}'",
        RailroadNonterminal nonterminal => $"nonterminal '{nonterminal.Name}'",
        _ => expression.GetType().Name,
    };

    /// <summary>Quotes a text with the backslash escapes of EBNF, PEG and the constructors.</summary>
    private static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            builder.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => c.ToString(),
            });
        }

        return builder.Append('"').ToString();
    }

    private static string Name(string name, RailroadNotation notation, RailroadExpression expression)
    {
        if (!RailroadDiagramParser.IsValidIdentifier(name, notation))
            throw CannotWrite(expression, notation.ToString());

        return name;
    }

    // --- Constructors

    private static string WritePrimitive(RailroadExpression expression, int depth)
    {
        CheckDepth(depth);
        expression = Simplify(expression, depth);
        return expression switch
        {
            RailroadTerminal terminal => "terminal(" + Quote(terminal.Value) + ")",
            RailroadNonterminal nonterminal => "nonterminal(" + Quote(nonterminal.Name) + ")",
            RailroadSpecial special => "special(" + Quote(special.Text) + ")",
            RailroadSequence sequence => "sequence(" + string.Join(", ", sequence.Items.Select(item => WritePrimitive(item, depth + 1))) + ")",
            RailroadChoice choice => "choice(" + string.Join(", ", choice.Alternatives.Select(item => WritePrimitive(item, depth + 1))) + ")",
            RailroadOptional optional => "optional(" + WritePrimitive(optional.Expression, depth + 1) + ")",
            RailroadRepetition { Minimum: 0, Maximum: null } repetition => "zeroOrMore(" + WritePrimitive(repetition.Expression, depth + 1) + ")",
            RailroadRepetition { Minimum: 1, Maximum: null } repetition => "oneOrMore(" + WritePrimitive(repetition.Expression, depth + 1) + ")",
            _ => throw CannotWrite(expression, "the constructors of railroad-beta"),
        };
    }

    // --- EBNF

    private static string WriteEbnfChoice(RailroadExpression expression, int depth)
    {
        CheckDepth(depth);
        expression = Simplify(expression, depth);
        return expression is RailroadChoice choice ? string.Join(" | ", choice.Alternatives.Select(item => WriteEbnfSequence(item, depth + 1))) : WriteEbnfSequence(expression, depth + 1);
    }

    private static string WriteEbnfSequence(RailroadExpression expression, int depth)
    {
        CheckDepth(depth);
        expression = Simplify(expression, depth);
        return expression is RailroadSequence sequence ? string.Join(' ', sequence.Items.Select(item => WriteEbnfTerm(item, depth + 1))) : WriteEbnfTerm(expression, depth + 1);
    }

    private static string WriteEbnfTerm(RailroadExpression expression, int depth)
    {
        CheckDepth(depth);
        expression = Simplify(expression, depth);
        return expression switch
        {
            RailroadTerminal terminal => Quote(terminal.Value),
            RailroadNonterminal nonterminal => Name(nonterminal.Name, RailroadNotation.Ebnf, expression),
            RailroadSpecial special when IsEbnfSpecial(special.Text) => "? " + special.Text + " ?",
            RailroadOptional optional => "[ " + WriteEbnfChoice(optional.Expression, depth + 1) + " ]",
            RailroadRepetition { Minimum: 0, Maximum: null } repetition => "{ " + WriteEbnfChoice(repetition.Expression, depth + 1) + " }",
            RailroadRepetition { Minimum: 1, Maximum: null } repetition => WriteEbnfTerm(repetition.Expression, depth + 1) + "+",
            RailroadSequence or RailroadChoice => "( " + WriteEbnfChoice(expression, depth + 1) + " )",
            _ => throw CannotWrite(expression, "EBNF"),
        };
    }

    /// <summary>Indicates whether a text can be written as a special sequence of EBNF, <c>? text ?</c>, and read back the same.</summary>
    private static bool IsEbnfSpecial(string text) => text.Length > 0 && text.Trim().Length == text.Length && text.AsSpan().IndexOfAny("?;\n\r") < 0;

    // --- ABNF

    private static string WriteAbnfAlternation(RailroadExpression expression, int depth)
    {
        CheckDepth(depth);
        expression = Simplify(expression, depth);
        return expression is RailroadChoice choice ? string.Join(" / ", choice.Alternatives.Select(item => WriteAbnfConcatenation(item, depth + 1))) : WriteAbnfConcatenation(expression, depth + 1);
    }

    private static string WriteAbnfConcatenation(RailroadExpression expression, int depth)
    {
        CheckDepth(depth);
        expression = Simplify(expression, depth);
        return expression is RailroadSequence sequence ? string.Join(' ', sequence.Items.Select(item => WriteAbnfElement(item, depth + 1))) : WriteAbnfElement(expression, depth + 1);
    }

    private static string WriteAbnfElement(RailroadExpression expression, int depth)
    {
        CheckDepth(depth);
        expression = Simplify(expression, depth);
        if (expression is not RailroadRepetition repetition)
            return WriteAbnfPrimary(expression, depth + 1);

        var minimum = repetition.Minimum.ToString(CultureInfo.InvariantCulture);
        var prefix = (repetition.Minimum, repetition.Maximum) switch
        {
            (0, null) => "*",
            (_, null) => minimum + "*",
            (0, var maximum) => "*" + maximum.Value.ToString(CultureInfo.InvariantCulture),
            (var min, var maximum) when min == maximum => minimum,
            (_, var maximum) => minimum + "*" + maximum.Value.ToString(CultureInfo.InvariantCulture),
        };
        return prefix + WriteAbnfPrimary(repetition.Expression, depth + 1);
    }

    private static string WriteAbnfPrimary(RailroadExpression expression, int depth)
    {
        CheckDepth(depth);
        expression = Simplify(expression, depth);
        return expression switch
        {
            RailroadTerminal terminal when RailroadDiagramParser.MatchNumericValue(terminal.Value, 0) == terminal.Value.Length && terminal.Value.Length > 0 => terminal.Value,
            RailroadTerminal terminal when terminal.Value.AsSpan().IndexOfAny("\"\n\r") < 0 => "\"" + terminal.Value + "\"",
            RailroadNonterminal nonterminal => Name(nonterminal.Name, RailroadNotation.Abnf, expression),
            RailroadOptional optional => "[ " + WriteAbnfAlternation(optional.Expression, depth + 1) + " ]",
            RailroadSequence or RailroadChoice or RailroadRepetition => "( " + WriteAbnfAlternation(expression, depth + 1) + " )",
            _ => throw CannotWrite(expression, "ABNF"),
        };
    }

    // --- PEG

    private static string WritePegChoice(RailroadExpression expression, int depth)
    {
        CheckDepth(depth);
        expression = Simplify(expression, depth);
        return expression is RailroadChoice choice ? string.Join(" / ", choice.Alternatives.Select(item => WritePegSequence(item, depth + 1))) : WritePegSequence(expression, depth + 1);
    }

    private static string WritePegSequence(RailroadExpression expression, int depth)
    {
        CheckDepth(depth);
        expression = Simplify(expression, depth);
        return expression is RailroadSequence sequence ? string.Join(' ', sequence.Items.Select(item => WritePegPrefix(item, depth + 1))) : WritePegPrefix(expression, depth + 1);
    }

    private static string WritePegPrefix(RailroadExpression expression, int depth)
    {
        CheckDepth(depth);
        expression = Simplify(expression, depth);
        if (expression is RailroadSpecial special && special.Text.Length > 1 && special.Text[0] is '&' or '!')
            return WritePegPredicate(special, depth + 1);

        return WritePegSuffix(expression, depth + 1);
    }

    /// <summary>Writes a predicate, <c>&amp;a</c> or <c>!a</c>: an expression the parser labels with the rest of the text.</summary>
    private static string WritePegPredicate(RailroadSpecial special, int depth)
    {
        CheckDepth(depth);
        var predicate = special.Text[0];
        var label = special.Text[1..];
        if (label == "(...)")
            return predicate + "(. .)";

        if (label == ".")
            return predicate + ".";

        if (RailroadDiagramParser.IsValidIdentifier(label, RailroadNotation.Peg))
            return predicate + label;

        if (label.Length >= 2 && label[0] == '"' && label[^1] == '"')
            return predicate + Quote(label[1..^1]);

        if (label.Length > 1 && label[0] is '&' or '!')
            return predicate + "(" + WritePegPredicate(new RailroadSpecial(label), depth + 1) + ")";

        throw CannotWrite(special, "PEG");
    }

    private static string WritePegSuffix(RailroadExpression expression, int depth)
    {
        CheckDepth(depth);
        expression = Simplify(expression, depth);
        return expression switch
        {
            RailroadOptional optional => WritePegPrimary(optional.Expression, depth + 1) + "?",
            RailroadRepetition { Minimum: 0, Maximum: null } repetition => WritePegPrimary(repetition.Expression, depth + 1) + "*",
            RailroadRepetition { Minimum: 1, Maximum: null } repetition => WritePegPrimary(repetition.Expression, depth + 1) + "+",
            RailroadRepetition => throw CannotWrite(expression, "PEG"),
            _ => WritePegPrimary(expression, depth + 1),
        };
    }

    private static string WritePegPrimary(RailroadExpression expression, int depth)
    {
        CheckDepth(depth);
        expression = Simplify(expression, depth);
        return expression switch
        {
            RailroadTerminal terminal => Quote(terminal.Value),
            RailroadNonterminal nonterminal => Name(nonterminal.Name, RailroadNotation.Peg, expression),
            RailroadSpecial { Text: "." } => ".",
            RailroadSpecial special when special.Text.Length > 1 && special.Text[0] is '&' or '!' => "(" + WritePegPredicate(special, depth + 1) + ")",
            RailroadSequence or RailroadChoice or RailroadOptional or RailroadRepetition => "(" + WritePegChoice(expression, depth + 1) + ")",
            _ => throw CannotWrite(expression, "PEG"),
        };
    }
}
