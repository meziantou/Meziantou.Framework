namespace Meziantou.Framework.Diagrams;

/// <summary>A choice of a railroad diagram: alternatives drawn one below the other, between two branches.</summary>
public sealed class RailroadChoice : RailroadExpression
{
    /// <summary>Initializes a new instance of the <see cref="RailroadChoice"/> class.</summary>
    /// <param name="alternatives">The alternatives.</param>
    public RailroadChoice(params IEnumerable<RailroadExpression> alternatives)
    {
        ArgumentNullException.ThrowIfNull(alternatives);
        foreach (var alternative in alternatives)
        {
            ArgumentNullException.ThrowIfNull(alternative, nameof(alternatives));
            Alternatives.Add(alternative);
        }
    }

    /// <summary>Gets the alternatives, in order.</summary>
    public IList<RailroadExpression> Alternatives { get; } = new List<RailroadExpression>();
}
