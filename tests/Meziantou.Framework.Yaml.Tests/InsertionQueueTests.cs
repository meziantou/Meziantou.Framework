namespace Meziantou.Framework.Yaml.Tests;

public class InsertionQueueTests
{
    [Fact]
    public void ShouldThrowExceptionWhenDequeuingEmptyContainer()
    {
        var queue = CreateQueue();

        Assert.Throws<InvalidOperationException>(() => queue.Dequeue());
    }

    [Fact]
    public void ShouldThrowExceptionWhenDequeuingContainerThatBecomesEmpty()
    {
        var queue = new InsertionQueue<int>();

        queue.Enqueue(1);
        queue.Dequeue();

        Assert.Throws<InvalidOperationException>(() => queue.Dequeue());
    }

    [Fact]
    public void ShouldCorrectlyDequeueElementsAfterEnqueuing()
    {
        var queue = CreateQueue();

        WithTheRange(0, 10).Perform(queue.Enqueue);

        Assert.Equal(new List<int>() { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, OrderOfElementsIn(queue).ToList());
    }

    [Fact]
    public void ShouldCorrectlyDequeueElementsWhenIntermixingEnqueuing()
    {
        var queue = CreateQueue();

        WithTheRange(0, 10).Perform(queue.Enqueue);
        PerformTimes(5, queue.Dequeue);
        WithTheRange(10, 15).Perform(queue.Enqueue);

        Assert.Equal(new List<int>() { 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 }, OrderOfElementsIn(queue).ToList());
    }

    [Fact]
    public void ShouldThrowExceptionWhenDequeuingAfterInserting()
    {
        var queue = CreateQueue();

        queue.Enqueue(1);
        queue.Insert(0, 99);
        PerformTimes(2, queue.Dequeue);

        Assert.Throws<InvalidOperationException>(() => queue.Dequeue());
    }

    [Fact]
    public void ShouldCorrectlyDequeueElementsWhenInserting()
    {
        var queue = CreateQueue();

        WithTheRange(0, 10).Perform(queue.Enqueue);
        queue.Insert(5, 99);

        Assert.Equal(new List<int>() { 0, 1, 2, 3, 4, 99, 5, 6, 7, 8, 9 }, OrderOfElementsIn(queue).ToList());
    }

    [Fact]
    public void ShouldCorrectlyInsertAfterDequeuingManyItems()
    {
        var queue = CreateQueue();

        WithTheRange(0, 100).Perform(queue.Enqueue);
        PerformTimes(70, queue.Dequeue); // triggers internal compaction in optimized implementation
        queue.Insert(5, 999);

        var expected = new List<int>();
        expected.AddRange(Enumerable.Range(70, 5));
        expected.Add(999);
        expected.AddRange(Enumerable.Range(75, 25));

        Assert.Equal(expected, OrderOfElementsIn(queue).ToList());
    }

    private static InsertionQueue<int> CreateQueue()
    {
        return new InsertionQueue<int>();
    }

    private static IEnumerable<int> WithTheRange(int from, int to)
    {
        return Enumerable.Range(@from, to - @from);
    }

    private static IEnumerable<int> OrderOfElementsIn(InsertionQueue<int> queue)
    {
        while (true)
        {
            if (queue.Count is 0)
            {
                yield break;
            }

            yield return queue.Dequeue();
        }
    }

    private static void PerformTimes(int times, Func<int> func)
    {
        WithTheRange(0, times).Perform(func);
    }
}

file static class EnumerableExtensions
{
    public static void Perform<T>(this IEnumerable<T> withRange, Func<int> func)
    {
        withRange.Perform(x => func());
    }

    public static void Perform<T>(this IEnumerable<T> withRange, Action<T> action)
    {
        foreach (var element in withRange)
        {
            action(element);
        }
    }
}
