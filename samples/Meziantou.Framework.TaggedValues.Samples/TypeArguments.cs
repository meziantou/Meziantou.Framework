namespace Sample;

public sealed class TypeArguments
{
    public void M()
    {
        #region TaggedValues_TypeArguments
        var projectIdByOrderId = new Dictionary</* ValueTag=OrderId */ Guid, /* ValueTag=ProjectId */ Guid>();
        var orderIds = new List</* ValueTag=OrderId */ Guid>();
        Dictionary</* ValueTag=OrderId */ Guid, /* ValueTag=ProjectId */ Guid> map = GetMap();
        #endregion
    }

    private static Dictionary<Guid, Guid> GetMap() => [];
}
