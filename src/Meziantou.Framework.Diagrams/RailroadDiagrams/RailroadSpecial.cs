namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A special sequence of a railroad diagram: a description of what the grammar cannot express, drawn as <c>? text ?</c>
/// (EBNF <c>? text ?</c>). The predicates of PEG (<c>&amp;a</c>, <c>!a</c>) and its <c>.</c> are special sequences too.
/// </summary>
public sealed class RailroadSpecial : RailroadExpression
{
    private string _text;

    /// <summary>Initializes a new instance of the <see cref="RailroadSpecial"/> class.</summary>
    /// <param name="text">The description.</param>
    public RailroadSpecial(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
    }

    /// <summary>Gets or sets the description.</summary>
    public string Text
    {
        get => _text;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _text = value;
        }
    }
}
