namespace Sample.Tagging;

#region TaggedValues_TagDeclarations
public sealed class Shipment([ValueTag("OrderId")] Guid orderId) // a parameter
{
    [ValueTag("OrderId")] private readonly Guid _orderId = orderId; // a field

    [ValueTag("ShipmentId")] public Guid Id { get; } = Guid.NewGuid(); // a property

    [return: ValueTag("OrderId")] // a return value
    public Guid GetOrderId() => _orderId;

    // A union: the parameter accepts an OrderId or a ProjectId
    public bool Concerns([ValueTag("OrderId", "ProjectId")] Guid id) => id == _orderId;
}
#endregion

#region TaggedValues_AsyncAndIterators
public sealed class ShipmentFactory
{
    [return: ValueTag("ShipmentId")] // the result of the task is a ShipmentId
    public async Task<Guid> CreateAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken);
        return Guid.NewGuid();
    }

    [return: ValueTag("ShipmentId")] // every element is a ShipmentId
    public IEnumerable<Guid> CreateMany(int count)
    {
        for (var i = 0; i < count; i++)
            yield return Guid.NewGuid();
    }
}
#endregion
