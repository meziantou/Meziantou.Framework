namespace Sample.Rules;

#region TaggedValues_MFTV0005
public sealed class Shipment
{
    [ValueTag("")] public Guid Id { get; set; }                         // MFTV0005, the tag is empty
    [ValueTag(Key = "OrderId")] public Guid OrderId { get; set; }       // MFTV0005, Key only applies to dictionaries
    [field: ValueTag("CarrierId")] public Guid CarrierId { get; set; }  // MFTV0005, the backing field is not analyzed
    /* ValueTag=ProjectId */ public Guid ProjectId { get; set; }        // MFTV0005, comments only tag local variables

    public void M()
    {
        Guid /* ValueTag= */ id = Guid.NewGuid();                       // MFTV0005, the comment cannot be parsed
    }
}
#endregion
