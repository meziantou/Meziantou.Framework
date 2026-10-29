namespace Sample.Rules.Fixed;

#region TaggedValues_MFTV0005_Fixed
public sealed class Shipment
{
    [ValueTag("ShipmentId")] public Guid Id { get; set; }
    [ValueTag("OrderId")] public Guid OrderId { get; set; }
    [ValueTag("CarrierId")] public Guid CarrierId { get; set; }
    [ValueTag("ProjectId")] public Guid ProjectId { get; set; }

    public void M()
    {
        Guid /* ValueTag=ShipmentId */ id = Guid.NewGuid();
    }
}
#endregion
