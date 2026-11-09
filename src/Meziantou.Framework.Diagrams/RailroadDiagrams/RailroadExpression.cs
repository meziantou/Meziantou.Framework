namespace Meziantou.Framework.Diagrams;

/// <summary>An expression of a rule of a railroad diagram: a terminal, a nonterminal, a sequence, a choice, an optional part, a repetition or a special sequence.</summary>
public abstract class RailroadExpression
{
    private protected RailroadExpression()
    {
    }

    /// <summary>Creates a terminal: a literal text, drawn in a rounded box.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The terminal.</returns>
    public static RailroadTerminal Terminal(string value) => new(value);

    /// <summary>Creates a nonterminal: a reference to a rule, drawn in a box.</summary>
    /// <param name="name">The name of the rule.</param>
    /// <returns>The nonterminal.</returns>
    public static RailroadNonterminal Nonterminal(string name) => new(name);

    /// <summary>Creates a sequence: expressions one after the other.</summary>
    /// <param name="items">The expressions.</param>
    /// <returns>The sequence.</returns>
    public static RailroadSequence Sequence(params IEnumerable<RailroadExpression> items) => new(items);

    /// <summary>Creates a choice: expressions of which one applies.</summary>
    /// <param name="alternatives">The alternatives.</param>
    /// <returns>The choice.</returns>
    public static RailroadChoice Choice(params IEnumerable<RailroadExpression> alternatives) => new(alternatives);

    /// <summary>Creates an optional expression: zero or one time.</summary>
    /// <param name="expression">The expression.</param>
    /// <returns>The optional expression.</returns>
    public static RailroadOptional Optional(RailroadExpression expression) => new(expression);

    /// <summary>Creates a repetition of zero or more times.</summary>
    /// <param name="expression">The repeated expression.</param>
    /// <returns>The repetition.</returns>
    public static RailroadRepetition ZeroOrMore(RailroadExpression expression) => new(expression, 0, maximum: null);

    /// <summary>Creates a repetition of one or more times.</summary>
    /// <param name="expression">The repeated expression.</param>
    /// <returns>The repetition.</returns>
    public static RailroadRepetition OneOrMore(RailroadExpression expression) => new(expression, 1, maximum: null);

    /// <summary>Creates a special sequence: a description of what cannot be written in the grammar, drawn as <c>? text ?</c>.</summary>
    /// <param name="text">The description.</param>
    /// <returns>The special sequence.</returns>
    public static RailroadSpecial Special(string text) => new(text);
}
