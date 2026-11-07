namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The kind of an Array Iterator ([[ArrayLikeIterationKind]]).</summary>
internal enum ArrayIteratorKind : byte
{
    Keys,
    Values,
    Entries,
}

/// <summary>
/// An object iterated by an Array Iterator with its own length rule. A TypedArray implements it: the length is
/// TypedArrayLength, and a TypeError is thrown when the typed array is out of bounds (detached buffer).
/// </summary>
internal interface IArrayIteratorSource
{
    /// <summary>Gets the length of the object for the next step of the iteration, or throws a TypeError.</summary>
    long GetIteratorLength();
}

/// <summary>An Array Iterator object (CreateArrayIterator): the iterated object, the kind and the next index.</summary>
internal sealed class JsArrayIterator : JsObject
{
    private JsArrayIterator(JsObject prototype, JsObject iterated, ArrayIteratorKind kind)
        : base(prototype, ObjectClass.Iterator)
    {
        Iterated = iterated;
        IterationKind = kind;
    }

    /// <summary>Gets or sets the iterated object, or null once the iterator is done.</summary>
    public JsObject? Iterated { get; set; }

    public ArrayIteratorKind IterationKind { get; }

    public long NextIndex { get; set; }

    /// <summary>Gets or sets whether the closure is running ([[GeneratorState]] is executing).</summary>
    public bool IsExecuting { get; set; }

    /// <summary>CreateArrayIterator(array, kind), in the realm of the running built-in.</summary>
    public static JsArrayIterator Create(JsRealm realm, JsObject array, ArrayIteratorKind kind) => new(realm.Intrinsics.ArrayIteratorPrototype, array, kind);
}
