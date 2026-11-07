// The tests of the analyzer analyze the files of this folder with taggedvalues.strict = true
namespace Sample.Strict;

#region TaggedValues_StrictMode
public sealed class Order
{
    [ValueTag("OrderId")] public Guid Id { get; set; } = Guid.NewGuid(); // ok, a new value
}

public sealed class OrderService
{
    private readonly Guid _other = Guid.NewGuid();

    public void M(Order order)
    {
        _ = order.Id == _other;     // MFTV0009, compared with an untagged value
        order.Id = _other;          // MFTV0009, an untagged value flows to a tagged declaration
        Load(order.Id);             // MFTV0009, a tagged value flows to an untagged declaration
        _ = order.Id == Guid.Empty; // ok
    }

    private static void Load(Guid id) { }
}
#endregion
