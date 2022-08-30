namespace Meziantou.Framework;

public static partial class EnumerableExtensions
{
    public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new AsyncEnumerableWrapper<T>(source);
    }

    public static IAsyncEnumerator<T> ToAsyncEnumerator<T>(this IEnumerator<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new AsyncEnumeratorWrapper<T>(source);
    }

    private sealed class AsyncEnumerableWrapper<T> : IAsyncEnumerable<T>
    {
        private readonly IEnumerable<T> _enumerable;

        public AsyncEnumerableWrapper(IEnumerable<T> enumerable)
        {
            _enumerable = enumerable;
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => new AsyncEnumeratorWrapper<T>(_enumerable.GetEnumerator());
    }

    private sealed class AsyncEnumeratorWrapper<T> : IAsyncEnumerator<T>
    {
        private readonly IEnumerator<T> _enumerator;

        public AsyncEnumeratorWrapper(IEnumerator<T> enumerator)
        {
            _enumerator = enumerator;
        }

        public T Current => _enumerator.Current;

        public ValueTask DisposeAsync()
        {
            _enumerator.Dispose();
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(_enumerator.MoveNext());
    }
}
