using SampleApp.Domain;

namespace SampleApp.Application;

public sealed class PlaceOrderHandler(IOrderRepository repository)
{
    public async Task<Guid> HandleAsync(IEnumerable<OrderLine> lines, CancellationToken cancellationToken)
    {
        var order = new Order();
        foreach (var line in lines)
        {
            order.Lines.Add(line);
        }

        await repository.SaveAsync(order, cancellationToken);
        return order.Id;
    }
}
