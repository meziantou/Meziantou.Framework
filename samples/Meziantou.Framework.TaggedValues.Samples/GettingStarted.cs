namespace Sample;

public sealed class GettingStarted
{
    public Order Load([ValueTag("OrderId")] Guid orderId) => new() { Id = orderId };

    public void M(Order order, Order other)
    {
        #region TaggedValues_GettingStartedFixed
        _ = order.ProjectId == other.ProjectId; // ok, both are ProjectIds
        Load(order.Id);                         // ok, an OrderId flows to an OrderId
        #endregion
    }
}
