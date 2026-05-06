namespace Meziantou.Framework;

public static partial class EnumerableExtensions
{
    public static Task ParallelForEachAsync<TSource>(this IEnumerable<TSource> source, Func<TSource, Task> action)
    {
        return ParallelForEachAsync(source, action, CancellationToken.None);
    }

    public static Task ParallelForEachAsync<TSource>(this IEnumerable<TSource> source, Func<TSource, Task> action, CancellationToken cancellationToken)
    {
        return ParallelForEachAsync(source, Environment.ProcessorCount, action, cancellationToken);
    }

    public static Task ParallelForEachAsync<TSource>(this IEnumerable<TSource> source, int degreeOfParallelism, Func<TSource, Task> action)
    {
        return ParallelForEachAsync(source, degreeOfParallelism, action, CancellationToken.None);
    }

    public static Task ParallelForEachAsync<TSource>(this IEnumerable<TSource> source, int degreeOfParallelism, Func<TSource, Task> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        ArgumentNullException.ThrowIfNull(action);

        return Parallel.ForEachAsync(source, new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism, CancellationToken = cancellationToken }, (item, ct) => new ValueTask(action(item)));
    }
}
