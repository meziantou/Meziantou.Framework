namespace Meziantou.Framework.Diagrams;

/// <summary>A nonterminal of a railroad diagram: a reference to a rule, drawn in a box.</summary>
public sealed class RailroadNonterminal : RailroadExpression
{
    private string _name;

    /// <summary>Initializes a new instance of the <see cref="RailroadNonterminal"/> class.</summary>
    /// <param name="name">The name of the rule.</param>
    public RailroadNonterminal(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _name = name;
    }

    /// <summary>Gets or sets the name of the rule.</summary>
    public string Name
    {
        get => _name;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _name = value;
        }
    }
}
