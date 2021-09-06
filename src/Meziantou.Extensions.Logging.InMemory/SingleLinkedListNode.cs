namespace Meziantou.Extensions.Logging.InMemory
{
    internal sealed class SingleLinkedListNode<T>
    {
        public SingleLinkedListNode(T value)
        {
            Value = value;
        }

        public T Value { get; }

        public SingleLinkedListNode<T>? Next { get; set; }
    }
}
