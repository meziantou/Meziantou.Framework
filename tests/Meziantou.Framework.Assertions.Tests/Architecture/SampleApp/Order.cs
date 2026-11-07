namespace SampleApp.Domain;

public sealed class Order
{
    public Guid Id { get; } = Guid.NewGuid();

    public IList<OrderLine> Lines { get; } = [];

    public decimal Total => Lines.Sum(line => line.Price * line.Quantity);
}
