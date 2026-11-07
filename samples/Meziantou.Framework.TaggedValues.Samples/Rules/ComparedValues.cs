namespace Sample.Rules;

public sealed class ComparedValues
{
    public void Reported(Order order, Project project, List<Order> orders)
    {
        #region TaggedValues_MFTV0001
        _ = order.Id == project.Id;                         // MFTV0001
        _ = order.Id.Equals(project.Id);                    // MFTV0001
        _ = orders.Where(item => item.Id == project.Id);    // MFTV0001
        #endregion
    }

    public void Fixed(Order order, Project project, List<Order> orders)
    {
        #region TaggedValues_MFTV0001_Fixed
        _ = order.ProjectId == project.Id;
        _ = order.ProjectId.Equals(project.Id);
        _ = orders.Where(item => item.ProjectId == project.Id);
        #endregion
    }
}
