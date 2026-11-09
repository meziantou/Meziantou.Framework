namespace Meziantou.Framework.Diagrams;

/// <summary>A sequence of a railroad diagram: expressions one after the other, on one track.</summary>
public sealed class RailroadSequence : RailroadExpression
{
    /// <summary>Initializes a new instance of the <see cref="RailroadSequence"/> class.</summary>
    /// <param name="items">The expressions.</param>
    public RailroadSequence(params IEnumerable<RailroadExpression> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item, nameof(items));
            Items.Add(item);
        }
    }

    /// <summary>Gets the expressions, in order.</summary>
    public IList<RailroadExpression> Items { get; } = new List<RailroadExpression>();
}
