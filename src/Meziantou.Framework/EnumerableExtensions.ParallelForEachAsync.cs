using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

#if NET5_0 || NETSTANDARD2_0
using System.Collections.Concurrent;
using System.Linq;
#endif

namespace Meziantou.Framework
{
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

#if NET6_0
        public static Task ParallelForEachAsync<TSource>(this IEnumerable<TSource> source, int degreeOfParallelism, Func<TSource, Task> action, CancellationToken cancellationToken)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            return Parallel.ForEachAsync(source, new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism, CancellationToken = cancellationToken }, (item, ct) => new ValueTask(action(item)));
        }
#elif NET5_0 || NETSTANDARD2_0
        public static async Task ParallelForEachAsync<TSource>(this IEnumerable<TSource> source, int degreeOfParallelism, Func<TSource, Task> action, CancellationToken cancellationToken)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var exceptions = new ConcurrentBag<Exception>();
            var tasks = from partition in Partitioner.Create(source).GetPartitions(degreeOfParallelism)
                        select Task.Run(async () =>
                        {
                            using (partition)
                            {
                                while (partition.MoveNext())
                                {
                                    try
                                    {
                                        await action(partition.Current).ConfigureAwait(false);
                                    }
                                    catch (Exception ex)
                                    {
                                        exceptions.Add(ex);
                                    }
                                }
                            }
                        }, cancellationToken);

            await Task.WhenAll(tasks).ConfigureAwait(false);
            if (!exceptions.IsEmpty)
            {
                throw new AggregateException(exceptions);
            }
        }
#else
#error Platform not supported
#endif
    }
}
