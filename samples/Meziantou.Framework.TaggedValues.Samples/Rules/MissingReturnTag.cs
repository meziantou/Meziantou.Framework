namespace Sample.Rules;

#region TaggedValues_MFTV0008
public sealed class OrderLookup
{
    public Guid GetFirstOrderId(List<Order> orders) => orders[0].Id; // MFTV0008
}
#endregion
