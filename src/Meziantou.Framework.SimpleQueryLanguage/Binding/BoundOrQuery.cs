namespace Meziantou.Framework.SimpleQueryLanguage.Binding;

/// <summary>Represents a bound OR query that combines two queries with logical OR.</summary>
public sealed class BoundOrQuery : BoundQuery
{
    public BoundOrQuery(BoundQuery left, BoundQuery right)
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
