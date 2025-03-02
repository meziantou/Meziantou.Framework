namespace Meziantou.Framework.SimpleQueryLanguage.Binding;

public sealed class BoundNegatedQuery : BoundQuery
{
    public BoundNegatedQuery(BoundQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        Query = query;
    }

    public BoundQuery Query { get; }
}
