namespace Meziantou.Framework
{
    public static partial class EnumerableExtensions
    {
        public static async Task ForEachAsync<TSource>(this IEnumerable<TSource> source, Func<TSource, Task> action, CancellationToken cancellationToken = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            foreach (var item in source)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await action(item).ConfigureAwait(false);
            }
        }

        public static async Task ForEachAsync<TSource>(this IEnumerable<TSource> source, Func<TSource, CancellationToken, Task> action, CancellationToken cancellationToken = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            foreach (var item in source)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await action(item, cancellationToken).ConfigureAwait(false);
            }
        }

        public static async Task ForEachAsync<TSource>(this IEnumerable<TSource> source, Func<TSource, int, Task> action, CancellationToken cancellationToken = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var index = 0;
            foreach (var item in source)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await action(item, index).ConfigureAwait(false);
                index++;
            }
        }

        public static async Task ForEachAsync<TSource>(this IEnumerable<TSource> source, Func<TSource, int, CancellationToken, Task> action, CancellationToken cancellationToken = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var index = 0;
            foreach (var item in source)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await action(item, index, cancellationToken).ConfigureAwait(false);
                index++;
            }
        }
    }
}
