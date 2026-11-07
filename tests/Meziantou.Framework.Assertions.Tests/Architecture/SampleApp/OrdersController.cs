using SampleApp.Application;
using SampleApp.Domain;
using SampleApp.Infrastructure;

namespace SampleApp.Web;

public sealed class OrdersController(PlaceOrderHandler handler)
{
    public Task<Guid> PostAsync(OrderLine[] lines) => handler.HandleAsync(lines, CancellationToken.None);

    // Breaks the layers on purpose: the documentation shows how the layer rule reports it
    public static IOrderRepository CreateRepository() => new SqlOrderRepository();
}
