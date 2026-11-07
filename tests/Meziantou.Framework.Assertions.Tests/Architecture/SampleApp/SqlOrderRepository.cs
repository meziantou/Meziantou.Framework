using SampleApp.Domain;

namespace SampleApp.Infrastructure;

public sealed class SqlOrderRepository : IOrderRepository
{
    public Task SaveAsync(Order order, CancellationToken cancellationToken) => Task.CompletedTask;
}
