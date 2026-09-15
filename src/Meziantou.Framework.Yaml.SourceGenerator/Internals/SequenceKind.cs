namespace Meziantou.Framework.Yaml.SourceGeneration;

internal enum SequenceKind
{
    List,
    Enumerable,
    MutableCollection,
    Set,
    ImmutableArray,
    ImmutableList,
    ImmutableHashSet,

    // The following collections cannot be filled through ICollection<T>: the elements are read into a list, from which
    // the collection is created.
    Queue,

    /// <summary>A stack is enumerated from its top, so the elements read are pushed in reverse order.</summary>
    Stack,
    ConcurrentQueue,

    /// <summary>A stack is enumerated from its top, so the elements read are pushed in reverse order.</summary>
    ConcurrentStack,
    ConcurrentBag,
    FrozenSet,

    /// <summary>A value type whose default value, which has no array, is written as null.</summary>
    ArraySegment,
}
