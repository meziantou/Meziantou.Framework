namespace Meziantou.Framework.Diagrams;

/// <summary>An optional expression of a railroad diagram: a track that goes through the expression, and a bypass above it.</summary>
public sealed class RailroadOptional : RailroadExpression
{
    private RailroadExpression _expression;

    /// <summary>Initializes a new instance of the <see cref="RailroadOptional"/> class.</summary>
    /// <param name="expression">The optional expression.</param>
    public RailroadOptional(RailroadExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        _expression = expression;
    }

    /// <summary>Gets or sets the optional expression.</summary>
    public RailroadExpression Expression
    {
        get => _expression;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _expression = value;
        }
    }
}
