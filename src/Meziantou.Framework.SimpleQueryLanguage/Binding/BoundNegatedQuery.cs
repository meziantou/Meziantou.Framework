namespace Meziantou.Framework.SimpleQueryLanguage.Binding;

/// <summary>Represents a bound NOT query that negates another query.</summary>
public sealed class BoundNegatedQuery : BoundQuery
{
    public BoundNegatedQuery(BoundQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        Query = query;
    }

    /// <summary>Gets the query to negate.</summary>
    public BoundQuery Query { get; }
}
