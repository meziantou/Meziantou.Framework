namespace Sample.Strict.Fixed;

#region TaggedValues_StrictModeFixed
public sealed class OrderService
{
    [ValueTag("OrderId")] private readonly Guid _other = Guid.NewGuid();

    public void M(Order order)
    {
        _ = order.Id == _other;
        order.Id = _other;
        Load(order.Id);
    }

    private static void Load([ValueTag("OrderId")] Guid id) { }
}
#endregion
