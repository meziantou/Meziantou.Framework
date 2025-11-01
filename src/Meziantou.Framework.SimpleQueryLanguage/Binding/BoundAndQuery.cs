namespace Meziantou.Framework.SimpleQueryLanguage.Binding;

/// <summary>Represents a bound AND query that combines two queries with logical AND.</summary>
public sealed class BoundAndQuery : BoundQuery
{
    internal BoundAndQuery(BoundQuery left, BoundQuery right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        Left = left;
        Right = right;
    }

    /// <summary>Gets the left operand.</summary>
    public BoundQuery Left { get; }

    /// <summary>Gets the right operand.</summary>
    public BoundQuery Right { get; }
}
