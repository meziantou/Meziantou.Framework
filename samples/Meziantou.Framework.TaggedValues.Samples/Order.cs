namespace Sample;

#region TaggedValues_Overview
public sealed class Order
{
    [ValueTag("OrderId")] public Guid Id { get; set; }
    [ValueTag("ProjectId")] public Guid ProjectId { get; set; }
}

public sealed class OrderService
{
    public Order Load([ValueTag("OrderId")] Guid orderId) => new() { Id = orderId };

    public void M(Order order)
    {
        _ = order.Id == order.ProjectId; // MFTV0001: values with different tags are compared
        Load(order.ProjectId);           // MFTV0002: a ProjectId flows to a parameter that expects an OrderId
    }
}
#endregion
