namespace Sample.Rules;

#region TaggedValues_RulesModel
public sealed class Order
{
    [ValueTag("OrderId")] public Guid Id { get; set; }
    [ValueTag("ProjectId")] public Guid ProjectId { get; set; }
}

public sealed class Project
{
    [ValueTag("ProjectId")] public Guid Id { get; set; }
}

public sealed class OrderRepository
{
    public Order? Find([ValueTag("OrderId")] Guid orderId) => null;
}
#endregion
