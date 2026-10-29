namespace Sample.Tagging;

#region TaggedValues_Inheritance
public interface IParcelStore
{
    void Delete([ValueTag("ParcelId")] Guid id);
}

public sealed class ParcelStore : IParcelStore
{
    public void Delete(Guid id) { } // id is a ParcelId: the tag comes from the interface
}

public sealed record Parcel([ValueTag("ParcelId")] Guid Id, [ValueTag("ShipmentId")] Guid ShipmentId);

public static class ParcelCleanup
{
    public static void M(ParcelStore store, Parcel parcel)
    {
        store.Delete(parcel.Id);         // ok, the property Id is a ParcelId
        store.Delete(parcel.ShipmentId); // MFTV0002
    }
}
#endregion
