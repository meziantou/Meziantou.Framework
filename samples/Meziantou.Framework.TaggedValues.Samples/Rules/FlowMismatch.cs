namespace Sample.Rules;

public sealed class FlowMismatch
{
    public void Reported(OrderRepository repository, Order order, Project project)
    {
        #region TaggedValues_MFTV0002
        _ = repository.Find(project.Id); // MFTV0002, a ProjectId is passed where an OrderId is expected
        order.ProjectId = order.Id;      // MFTV0002, an OrderId is assigned to a ProjectId
        #endregion
    }

    public void Fixed(OrderRepository repository, Order order, Project project)
    {
        #region TaggedValues_MFTV0002_Fixed
        _ = repository.Find(order.Id);
        order.ProjectId = project.Id;
        #endregion
    }
}
