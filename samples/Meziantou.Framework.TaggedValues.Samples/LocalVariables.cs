namespace Sample;

public sealed class LocalVariables
{
    public void BlockComments(string path)
    {
        #region TaggedValues_LocalBlockComments
        /* ValueTag=OrderId */ Guid id1 = Guid.NewGuid();
        Guid /* ValueTag=OrderId */ id2 = Guid.NewGuid();
        var id3 /* ValueTag=OrderId */ = Guid.NewGuid();
        using var /* ValueTag=OrderFile */ stream = File.OpenRead(path);

        Guid /* ValueTag=OrderId */ a = Guid.NewGuid(), b = Guid.NewGuid();   // a and b are OrderIds
        Guid c = Guid.NewGuid(), d /* ValueTag=ProjectId */ = Guid.NewGuid(); // only d is a ProjectId
        Guid e = Guid.NewGuid(), /* ValueTag=ProjectId */ f = Guid.NewGuid(); // only f is a ProjectId

        var /* ValueTag Key=OrderId Value=ProjectId */ projectIdByOrderId = new Dictionary<Guid, Guid>();
        #endregion
    }

    public void LineComments()
    {
        #region TaggedValues_LocalLineComments
        // ValueTag=OrderId
        Guid id1 = Guid.NewGuid();

        // ValueTag=OrderId
        // The order being processed
        Guid a = Guid.NewGuid(), b /* ValueTag=ProjectId */ = Guid.NewGuid(); // a is an OrderId, b is a ProjectId

        // ValueTag=OrderId
        Guid c /* ValueTag=ProjectId */ = Guid.NewGuid(); // MFTV0005 on the // comment, which does not tag anything

        // ValueTag Key=OrderId Value=ProjectId
        var projectIdByOrderId = new Dictionary<Guid, Guid>();
        #endregion
    }

    public void OtherDeclarations(IEnumerable<Guid> ids, int count, object value)
    {
        #region TaggedValues_LocalOtherDeclarations
        // ValueTag=OrderId
        foreach (var id in ids) { }
        foreach (Guid /* ValueTag=OrderId */ id in ids) { }
        for (int /* ValueTag=OrderIndex */ i = 0; i < count; i++) { }
        if (TryGetId(out var /* ValueTag=OrderId */ parsedId)) { }
        if (value is Guid /* ValueTag=OrderId */ matchedId) { }
        var (orderId /* ValueTag=OrderId */, projectId) = GetIds();
        #endregion
    }

    public void Inference(Order order)
    {
        #region TaggedValues_LocalInference
        var id = order.Id;    // id is an OrderId
        Load(id);             // ok
        id = order.ProjectId; // MFTV0002

        var pair = (order.Id, order.ProjectId);
        Load(pair.Item1);     // ok
        Load(pair.Item2);     // MFTV0002
        #endregion
    }

    private static Order Load([ValueTag("OrderId")] Guid orderId) => new() { Id = orderId };

    private static bool TryGetId(out Guid id)
    {
        id = Guid.NewGuid();
        return true;
    }

    private static (Guid OrderId, Guid ProjectId) GetIds() => (Guid.NewGuid(), Guid.NewGuid());
}
