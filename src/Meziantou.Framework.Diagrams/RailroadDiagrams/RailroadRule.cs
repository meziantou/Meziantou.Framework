namespace Meziantou.Framework.Diagrams;

/// <summary>A rule of a railroad diagram: a name and the expression it stands for.</summary>
public sealed class RailroadRule
{
    private string _name;
    private RailroadExpression _definition;

    /// <summary>Initializes a new instance of the <see cref="RailroadRule"/> class.</summary>
    /// <param name="name">The name of the rule.</param>
    /// <param name="definition">The expression the rule stands for.</param>
    public RailroadRule(string name, RailroadExpression definition)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(definition);
        _name = name;
        _definition = definition;
    }

    /// <summary>Gets or sets the name of the rule, shown before its track.</summary>
    public string Name
    {
        get => _name;
        set
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            _name = value;
        }
    }

    /// <summary>Gets or sets the expression the rule stands for.</summary>
    public RailroadExpression Definition
    {
        get => _definition;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _definition = value;
        }
    }

    /// <summary>Returns the name of the rule.</summary>
    /// <returns>The name.</returns>
    public override string ToString() => Name;
}
