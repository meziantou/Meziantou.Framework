namespace Sample.Rules;

public sealed class CombinedValues
{
    public void Reported(Order order, Order other, Project project, bool useProject, [ValueTag("Meters")] double meters, [ValueTag("Feet")] double feet)
    {
        #region TaggedValues_MFTV0003
        var id = useProject ? order.ProjectId : order.Id; // MFTV0003
        Guid[] ids = [order.Id, order.ProjectId];         // MFTV0003
        var length = meters + feet;                       // MFTV0003
        #endregion
    }

    public void Fixed(Order order, Order other, Project project, bool useProject)
    {
        #region TaggedValues_MFTV0003_Fixed
        var id = useProject ? order.ProjectId : project.Id; // both are ProjectIds
        Guid[] ids = [order.Id, other.Id];                  // both are OrderIds
        #endregion
    }
}
