namespace Sample.Rules;

#region TaggedValues_MFTV0004
public interface IOrderStore
{
    Order? Find([ValueTag("OrderId")] Guid id);
}

public sealed class OrderStore : IOrderStore
{
    public Order? Find([ValueTag("ProjectId")] Guid id) => null; // MFTV0004
}
#endregion
