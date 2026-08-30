namespace Meziantou.Extensions.Logging.InMemory;

internal sealed class Chunk<T>
{
    private int _count;
    private Chunk<T>? _next;

    public Chunk(int capacity)
    {
        Items = new T[capacity];
    }

    public T[] Items { get; }

    /// <summary>Gets or sets the number of entries written to <see cref="Items"/>.</summary>
    /// <remarks>
    /// Written under the collection's lock but read without it, so both accessors are volatile.
    /// The release on the write publishes the preceding <see cref="Items"/> store to any reader
    /// that acquires the new count.
    /// </remarks>
    public int Count
    {
        get => Volatile.Read(ref _count);
        set => Volatile.Write(ref _count, value);
    }

    /// <summary>Gets or sets the next chunk of the collection.</summary>
    /// <remarks>Volatile for the same reason as <see cref="Count"/>: it publishes a populated chunk.</remarks>
    public Chunk<T>? Next
    {
        get => Volatile.Read(ref _next);
        set => Volatile.Write(ref _next, value);
    }
}
