namespace Sample;

public sealed class Projections
{
    public void M(List<Order> orders)
    {
        #region TaggedValues_Projections
        var rows = orders.Select(order => new { order.Id, order.ProjectId });
        foreach (var row in rows)
        {
            Load(row.Id);        // ok
            Load(row.ProjectId); // MFTV0002
        }

        var ids = new List<Guid> { orders[0].Id }; // a list of OrderIds
        Load(ids[0]);                              // ok
        #endregion
    }

    private static Order Load([ValueTag("OrderId")] Guid orderId) => new() { Id = orderId };
}
