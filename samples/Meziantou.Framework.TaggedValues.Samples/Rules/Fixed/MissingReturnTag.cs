namespace Sample.Rules.Fixed;

#region TaggedValues_MFTV0008_Fixed
public sealed class OrderLookup
{
    [return: ValueTag("OrderId")]
    public Guid GetFirstOrderId(List<Order> orders) => orders[0].Id;
}
#endregion
