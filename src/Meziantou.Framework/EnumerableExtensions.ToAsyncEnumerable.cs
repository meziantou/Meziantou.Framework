using System.ComponentModel;

namespace Meziantou.Framework;

public static partial class EnumerableExtensions
{
    /// <summary>Wraps an <see cref="IEnumerable{T}"/> into an <see cref="IAsyncEnumerable{T}"/>.</summary>
    /// <remarks>
    /// This method is not an extension method anymore, so that <c>source.ToAsyncEnumerable()</c> calls
    /// <see cref="System.Linq.AsyncEnumerable.ToAsyncEnumerable{TSource}(IEnumerable{TSource})"/> instead of being ambiguous with it.
    /// </remarks>
    [Obsolete("Use System.Linq.AsyncEnumerable.ToAsyncEnumerable instead.")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new AsyncEnumerableWrapper<T>(source);
    }

    /// <summary>Wraps an <see cref="IEnumerator{T}"/> into an <see cref="IAsyncEnumerator{T}"/>.</summary>
    public static IAsyncEnumerator<T> ToAsyncEnumerator<T>(this IEnumerator<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new AsyncEnumeratorWrapper<T>(source, CancellationToken.None);
    }

    private sealed class AsyncEnumerableWrapper<T> : IAsyncEnumerable<T>
    {
        private readonly IEnumerable<T> _enumerable;

        public AsyncEnumerableWrapper(IEnumerable<T> enumerable)
        {
            _enumerable = enumerable;
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new AsyncEnumeratorWrapper<T>(_enumerable.GetEnumerator(), cancellationToken);
        }
    }

    private sealed class AsyncEnumeratorWrapper<T> : IAsyncEnumerator<T>
    {
        private readonly IEnumerator<T> _enumerator;
        private readonly CancellationToken _cancellationToken;

        public AsyncEnumeratorWrapper(IEnumerator<T> enumerator, CancellationToken cancellationToken)
        {
            _enumerator = enumerator;
            _cancellationToken = cancellationToken;
        }

        public T Current => _enumerator.Current;

        public ValueTask DisposeAsync()
        {
            _enumerator.Dispose();
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> MoveNextAsync()
        {
            if (_cancellationToken.IsCancellationRequested)
                return ValueTask.FromCanceled<bool>(_cancellationToken);

            return ValueTask.FromResult(_enumerator.MoveNext());
        }
    }
}
