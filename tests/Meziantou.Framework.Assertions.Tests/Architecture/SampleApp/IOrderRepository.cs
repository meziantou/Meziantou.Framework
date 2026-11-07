namespace SampleApp.Domain;

public interface IOrderRepository
{
    Task SaveAsync(Order order, CancellationToken cancellationToken);
}
