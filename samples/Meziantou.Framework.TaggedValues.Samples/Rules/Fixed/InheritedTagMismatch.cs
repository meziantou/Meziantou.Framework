namespace Sample.Rules.Fixed;

#region TaggedValues_MFTV0004_Fixed
public sealed class OrderStore : IOrderStore
{
    public Order? Find(Guid id) => null; // id inherits the OrderId tag of IOrderStore.Find
}
#endregion
