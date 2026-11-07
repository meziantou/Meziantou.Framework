namespace Sample;

public sealed class OrderCollections
{
    #region TaggedValues_Collections
    [ValueTag("OrderId")] public List<Guid> OrderIds { get; } = [];

    public void M(Order order)
    {
        OrderIds.Add(order.ProjectId);                        // MFTV0002
        _ = OrderIds.Where(id => id == order.ProjectId);      // MFTV0001
        Load(OrderIds.Where(id => id != Guid.Empty).First()); // ok
    }
    #endregion

    #region TaggedValues_Dictionaries
    [ValueTag(Key = "OrderId", Value = "ProjectId")]
    public Dictionary<Guid, Guid> ProjectIdByOrderId { get; } = [];
    #endregion

    private static Order Load([ValueTag("OrderId")] Guid orderId) => new() { Id = orderId };
}
