namespace Meziantou.Extensions.Logging.InMemory
{
    internal sealed class Chunk<T>
    {
        public Chunk(int capacity)
        {
            Items = GC.AllocateUninitializedArray<T>(capacity);
        }

        public T[] Items { get; set; }
        public int Count { get; set; }
        public Chunk<T>? Next { get; set; }
    }
}
