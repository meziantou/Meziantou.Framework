using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// A serializable platform object (https://html.spec.whatwg.org/multipage/structured-data.html#serializable-objects): the
/// clone is the serialized form of the object (taken when it is serialized), and a clone of the serialized form is the
/// deserialized object.
/// </summary>
internal interface IStructuredSerializable
{
    /// <summary>Returns a new object with the same data (the serialization and deserialization steps).</summary>
    object CloneForStructuredSerialization();
}

/// <summary>
/// A transferable platform object (https://html.spec.whatwg.org/multipage/structured-data.html#transferable-objects), e.g.
/// a <see cref="MessagePort"/>.
/// </summary>
internal interface IStructuredTransferable
{
    /// <summary>
    /// Throws a <c>DataCloneError</c> when the object cannot be transferred: called before the serialization of the value
    /// (<paramref name="serialized"/> is false, e.g. the source port of a message), then after it (e.g. a detached object,
    /// https://html.spec.whatwg.org/multipage/structured-data.html#structuredserializewithtransfer).
    /// </summary>
    void ValidateTransfer(bool serialized);

    /// <summary>The transfer steps: detaches the object, and returns the data from which the transfer-receiving steps create the transferred object.</summary>
    ITransferredData Transfer();
}

/// <summary>The data of a transferred object, given to the transfer-receiving steps (possibly on the thread of another agent).</summary>
internal interface ITransferredData
{
    /// <summary>The transfer-receiving steps: creates the transferred object in the realm of <paramref name="target"/> (once: the next calls return the same object).</summary>
    object Receive(GlobalScope? target);
}

/// <summary>
/// The structured serialization of the .NET values (https://html.spec.whatwg.org/multipage/structured-data.html#structuredserialize),
/// used when there is no script engine: the primitive values (null, strings, booleans, numbers) and the serializable
/// platform objects. A script engine serializes the JavaScript values (see <see cref="ScriptEngine.StructuredSerialize(Dom.GlobalScope, object?)"/>).
/// </summary>
internal static class StructuredClone
{
    public static object? Serialize(object? value) => value switch
    {
        null or string or bool or double or float or int or uint or long or ulong or short or ushort or byte or sbyte or decimal or System.Numerics.BigInteger => value,
        IStructuredSerializable serializable => serializable.CloneForStructuredSerialization(),
        _ => throw new DomException($"An object of type '{value.GetType().Name}' could not be cloned", "DataCloneError"),
    };

    public static object? Deserialize(object? serialized) => serialized switch
    {
        IStructuredSerializable serializable => serializable.CloneForStructuredSerialization(),
        ITransferredData transferred => transferred.Receive(s_deserializationTarget ?? GlobalScope.Current),
        _ => serialized,
    };

    [ThreadStatic]
    private static List<ITransferredData>? s_transferred;

    [ThreadStatic]
    private static GlobalScope? s_deserializationTarget;

    /// <summary>Runs the transfer steps of an object, and records the data in the current <see cref="CollectTransfers"/> scope.</summary>
    public static ITransferredData Transfer(IStructuredTransferable transferable)
    {
        var data = transferable.Transfer();
        s_transferred?.Add(data);
        return data;
    }

    /// <summary>Records the data of the objects transferred until the scope is disposed (e.g. the message ports of a message).</summary>
    public static TransferCollector CollectTransfers()
    {
        var previous = s_transferred;
        s_transferred = [];
        return new TransferCollector(previous);
    }

    /// <summary>Sets the global object in which the transferred objects are received until the scope is disposed.</summary>
    public static DeserializationScope EnterDeserialization(GlobalScope target)
    {
        var previous = s_deserializationTarget;
        s_deserializationTarget = target;
        return new DeserializationScope(previous);
    }

    internal readonly struct TransferCollector(List<ITransferredData>? previous) : IDisposable
    {
        public IReadOnlyList<ITransferredData> Transferred => s_transferred ?? [];

        public void Dispose() => s_transferred = previous;
    }

    internal readonly struct DeserializationScope(GlobalScope? previous) : IDisposable
    {
        public void Dispose() => s_deserializationTarget = previous;
    }
}
