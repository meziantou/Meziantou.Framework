using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Tests;

public class EnumerableTests
{
    [Fact]
    public void ReplaceTests_01()
    {
        // Arrange
        var list = new List<int>() { 1, 2, 3 };

        // Act
        list.Replace(2, 5);
        Assert.Equal(new List<int> { 1, 5, 3 }, list);
    }

    [Fact]
    public void ReplaceTests_02()
    {
        // Arrange
        var list = new List<int>() { 1, 2, 3 };

        // Act
        Assert.Throws<ArgumentOutOfRangeException>(() => list.Replace(10, 5));
    }

    [Fact]
    public void AddOrReplaceTests_01()
    {
        // Arrange
        var list = new List<int>() { 1, 2, 3 };

        // Act
        list.AddOrReplace(10, 5);
        Assert.Equal([1, 2, 3, 5], list);
    }

    [Fact]
    public void AddOrReplaceTests_02()
    {
        // Arrange
        var list = new List<string>();

        // Act
        list.AddOrReplace(null, "");
        Assert.Equal([""], list);
    }

    [Fact]
    public void AddOrReplaceTests_03()
    {
        // Arrange
        var list = new List<int> { 1, 2, 3 };

        // Act
        list.AddOrReplace(2, 5);
        Assert.Equal([1, 5, 3], list);
    }

    [Fact]
    public async Task ForEachAsync()
    {
        var bag = new ConcurrentBag<int>();
        await Enumerable.Range(1, 100).ForEachAsync(async i =>
        {
            await Task.Yield();
            bag.Add(i);
        });

        Assert.HasCount(100, bag);
    }

    [Fact]
    public async Task ParallelForEachAsync()
    {
        var bag = new ConcurrentBag<int>();
        await Enumerable.Range(1, 100).ParallelForEachAsync(async i =>
        {
            await Task.Yield();
            bag.Add(i);
        });

        Assert.HasCount(100, bag);
    }

    [Fact]
    public void TimeSpan_Sum()
    {
        // Arrange
        var list = new List<TimeSpan>() { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(20) };

        // Act
        var sum = list.Sum();
        Assert.Equal(TimeSpan.FromSeconds(23), sum);
    }

    [Fact]
    public void TimeSpan_Average()
    {
        // Arrange
        var list = new List<TimeSpan>() { TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20) };

        // Act
        var sum = list.Average();
        Assert.Equal(TimeSpan.FromSeconds(9), sum);
    }

    [Fact]
    public void EmptyIfNull_Null()
    {
        IEnumerable<string>? items = null;
        Assert.Equal([], items.EmptyIfNull());
    }

    [Fact]
    public void EmptyIfNull_NotNull()
    {
        var items = new string[] { "" };
        Assert.Same(items, items.EmptyIfNull());
    }

#nullable enable
    [Fact]
    [SuppressMessage("Style", "IDE0007:Use implicit type", Justification = "Need to validate the type is non-nullable")]
    public void WhereNotNull()
    {
        // Arrange
        var list = new List<string?>() { "", null, "a" };

        // Act
        // Do not use var, so we can validate the nullable annotations
        List<string> actual = list.WhereNotNull().ToList();
        Assert.Equal(["", "a"], actual);
    }

    [Fact]
    [SuppressMessage("Style", "IDE0007:Use implicit type", Justification = "Need to validate the type is non-nullable")]
    public void WhereNotNull_Struct()
    {
        // Arrange
        var list = new List<int?>() { 0, null, 2 };

        // Act
        // Do not use var, so we can validate the nullable annotations
        List<int> actual = list.WhereNotNull().ToList();
        Assert.Equal([0, 2], actual);
    }
#nullable disable

    [Fact]
    public void ForeachEnumerator()
    {
        var items = new List<int>();
        foreach (var item in CustomEnumerator())
        {
            items.Add(item);
        }

        Assert.Equal([1, 2], items);

        static IEnumerator<int> CustomEnumerator()
        {
            yield return 1;
            yield return 2;
        }
    }

    [Fact]
    public async Task ForeachAsyncEnumerator()
    {
        var items = new List<int>();
        await foreach (var item in CustomEnumerator())
        {
            items.Add(item);
        }

        Assert.Equal([1, 2], items);

        static async IAsyncEnumerator<int> CustomEnumerator()
        {
            await Task.Yield();
            yield return 1;
            yield return 2;
        }
    }

    [Fact]
    public void IsDistinct_MultipleNulls()
    {
        var array = new[] { "a", null, null };
        Assert.False(array.IsDistinct(StringComparer.Ordinal));
    }

    [Fact]
    public void IsDistinct_MultipleIdenticalValues()
    {
        var array = new[] { "a", "b", "a" };
        Assert.False(array.IsDistinct(StringComparer.Ordinal));
    }

    [Fact]
    public void IsDistinct()
    {
        var array = new[] { "a", "b", "c" };
        Assert.True(array.IsDistinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ToListAsync()
    {
        var data = await GetDataAsync().ToListAsync();
        Assert.Equal(["a", "b", "c"], data);

        static async Task<IEnumerable<string>> GetDataAsync()
        {
            await Task.Yield();
            return ["a", "b", "c"];
        }
    }

    [Fact]
    public void AsEnumerableOnceTest()
    {
        var data = new[] { "a" }.AsEnumerableOnce();
        _ = data.ToList();
        Assert.Throws<InvalidOperationException>(() => data.ToList());
    }

    [Fact]
    public void ParallelSort_Span_SortsValues()
    {
        var values = Enumerable.Range(0, 20_000).Reverse().ToArray();
        values.AsSpan().ParallelSort();

        Assert.Equal(Enumerable.Range(0, 20_000).ToArray(), values);
    }

    [Fact]
    public void ParallelSort_Span_WithComparer()
    {
        int[] values = [1, 5, 2, 3, 4];
        values.AsSpan().ParallelSort(Comparer<int>.Create((left, right) => right.CompareTo(left)));

        Assert.Equal([5, 4, 3, 2, 1], values);
    }

    [Fact]
    public void ParallelSort_Span_WithDegreeOfParallelismAndComparer()
    {
        var values = Enumerable.Range(0, 20_000).Reverse().ToArray();
        values.AsSpan().ParallelSort(2, Comparer<int>.Default);

        Assert.Equal(Enumerable.Range(0, 20_000).ToArray(), values);
    }

    [Fact]
    public void ParallelSort_Array_DelegatesToSpan()
    {
        var values = Enumerable.Range(0, 20_000).Reverse().ToArray();
        values.ParallelSort(2);

        Assert.Equal(Enumerable.Range(0, 20_000).ToArray(), values);
    }

    [Fact]
    public void ParallelSort_List_DelegatesToSpan()
    {
        var values = Enumerable.Range(0, 20_000).Reverse().ToList();
        values.ParallelSort(2);

        Assert.Equal(Enumerable.Range(0, 20_000).ToArray(), values.ToArray());
    }

    [Fact]
    public void ParallelSort_List_WithComparer()
    {
        var values = new List<int> { 1, 5, 2, 3, 4 };
        values.ParallelSort(Comparer<int>.Create((left, right) => right.CompareTo(left)));

        Assert.Equal([5, 4, 3, 2, 1], values);
    }

    [Fact]
    public void ParallelSort_InvalidDegree_Throws()
    {
        int[] values = [2, 1];
        Assert.Throws<ArgumentOutOfRangeException>(() => values.AsSpan().ParallelSort(0));
    }

    [Fact]
    public void ParallelSort_NullArray_Throws()
    {
        int[] values = null;
        Assert.Throws<ArgumentNullException>(() => values!.ParallelSort());
    }

    [Fact]
    public void ParallelSort_NullList_Throws()
    {
        List<int> values = null!;
        Assert.Throws<ArgumentNullException>(() => values.ParallelSort());
    }

    [Fact]
    public void ParallelStableSort_PreservesOrderForEqualValues()
    {
        var values = new[]
        {
            new StableSortableValue(2, 0),
            new StableSortableValue(1, 1),
            new StableSortableValue(2, 2),
            new StableSortableValue(1, 3),
        };

        values.AsSpan().ParallelStableSort(2, StableSortableValueComparer.Instance);

        Assert.Equal(
            [
                new StableSortableValue(1, 1),
                new StableSortableValue(1, 3),
                new StableSortableValue(2, 0),
                new StableSortableValue(2, 2),
            ],
            values);
    }

    [Fact]
    public void ParallelStableSort_Array_DelegatesToSpan()
    {
        var values = new[]
        {
            new StableSortableValue(2, 0),
            new StableSortableValue(1, 1),
            new StableSortableValue(2, 2),
            new StableSortableValue(1, 3),
        };

        values.ParallelStableSort(2, StableSortableValueComparer.Instance);

        Assert.Equal(
            [
                new StableSortableValue(1, 1),
                new StableSortableValue(1, 3),
                new StableSortableValue(2, 0),
                new StableSortableValue(2, 2),
            ],
            values);
    }

    [Fact]
    public void ParallelStableSort_List_DelegatesToSpan()
    {
        var values = new List<StableSortableValue>
        {
            new StableSortableValue(2, 0),
            new StableSortableValue(1, 1),
            new StableSortableValue(2, 2),
            new StableSortableValue(1, 3),
        };

        values.ParallelStableSort(2, StableSortableValueComparer.Instance);

        Assert.Equal(
            [
                new StableSortableValue(1, 1),
                new StableSortableValue(1, 3),
                new StableSortableValue(2, 0),
                new StableSortableValue(2, 2),
            ],
            values);
    }

    [Fact]
    public void ParallelStableSort_WithCustomComparer_IsStable()
    {
        var values = Enumerable.Range(0, 10_000).Reverse().ToArray();
        var expected = values.Where(value => (value & 1) is 0).Concat(values.Where(value => (value & 1) is 1)).ToArray();

        values.AsSpan().ParallelStableSort(2, IntParityComparer.Instance);

        Assert.Equal(expected, values);
    }

    [Fact]
    public void ParallelStableSort_ImplicitStableType_DefaultComparer_Sorts()
    {
        var values = Enumerable.Range(0, 20_000).Reverse().ToArray();
        values.AsSpan().ParallelStableSort(2);

        Assert.Equal(Enumerable.Range(0, 20_000).ToArray(), values);
    }

    [Fact]
    public void ParallelStableSort_Enum_DefaultComparer_Sorts()
    {
        BranchCoverageEnum[] values = [BranchCoverageEnum.C, BranchCoverageEnum.A, BranchCoverageEnum.B];
        values.AsSpan().ParallelStableSort(2);

        Assert.Equal([BranchCoverageEnum.B, BranchCoverageEnum.A, BranchCoverageEnum.C], values);
    }

    [Fact]
    public void ParallelStableSort_ReferenceType_DefaultComparer_IsStable()
    {
        var values = new[]
        {
            new ComparableReferenceValue(2, 0),
            new ComparableReferenceValue(1, 1),
            new ComparableReferenceValue(2, 2),
            new ComparableReferenceValue(1, 3),
        };

        values.AsSpan().ParallelStableSort(2);

        Assert.Equal([1, 3, 0, 2], values.Select(value => value.Order).ToArray());
    }

    [Fact]
    public void ParallelStableSort_InvalidDegree_Throws()
    {
        int[] values = [2, 1];
        Assert.Throws<ArgumentOutOfRangeException>(() => values.AsSpan().ParallelStableSort(0));
    }

    [Fact]
    public void ParallelStableSort_NullArray_Throws()
    {
        int[] values = null;
        Assert.Throws<ArgumentNullException>(() => values!.ParallelStableSort());
    }

    [Fact]
    public void ParallelStableSort_NullList_Throws()
    {
        List<int> values = null!;
        Assert.Throws<ArgumentNullException>(() => values.ParallelStableSort());
    }

    [Fact]
    public async Task ToAsyncEnumerable_BindsToSystemLinq()
    {
        // Not ambiguous with System.Linq.AsyncEnumerable.ToAsyncEnumerable while Meziantou.Framework is in scope
        var actual = new[] { 1, 2 }.ToAsyncEnumerable();

        await Assert.Equal([1, 2], actual);
    }

    [Fact]
    public async Task ToAsyncEnumerable_HonorsCancellation()
    {
        using var cts = new CancellationTokenSource();
#pragma warning disable CS0618 // Type or member is obsolete
        var enumerable = EnumerableExtensions.ToAsyncEnumerable(new[] { 1, 2, 3 });
#pragma warning restore CS0618

        await using var enumerator = enumerable.GetAsyncEnumerator(cts.Token);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(1, enumerator.Current);

        await cts.CancelAsync();
        await Assert.ThrowsAny<OperationCanceledException>(async () => { await enumerator.MoveNextAsync(); });
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct StableSortableValue(int Key, int Order);

    private sealed class StableSortableValueComparer : IComparer<StableSortableValue>
    {
        public static StableSortableValueComparer Instance { get; } = new();

        public int Compare(StableSortableValue x, StableSortableValue y)
        {
            return x.Key.CompareTo(y.Key);
        }
    }

    private sealed class IntParityComparer : IComparer<int>
    {
        public static IntParityComparer Instance { get; } = new();

        public int Compare(int x, int y)
        {
            return (x & 1).CompareTo(y & 1);
        }
    }

    private sealed class ComparableReferenceValue(int key, int order) : IComparable<ComparableReferenceValue>, IEquatable<ComparableReferenceValue>
    {
        public int Key { get; } = key;
        public int Order { get; } = order;

        public int CompareTo(ComparableReferenceValue other)
        {
            if (other is null)
                return 1;

            return Key.CompareTo(other.Key);
        }

        public bool Equals(ComparableReferenceValue other)
        {
            if (other is null)
                return false;

            return Key == other.Key && Order == other.Order;
        }

        public override bool Equals(object obj)
        {
            return obj is ComparableReferenceValue other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Key, Order);
        }

        public static bool operator <(ComparableReferenceValue left, ComparableReferenceValue right)
        {
            return Comparer<ComparableReferenceValue>.Default.Compare(left, right) < 0;
        }

        public static bool operator <=(ComparableReferenceValue left, ComparableReferenceValue right)
        {
            return Comparer<ComparableReferenceValue>.Default.Compare(left, right) <= 0;
        }

        public static bool operator >(ComparableReferenceValue left, ComparableReferenceValue right)
        {
            return Comparer<ComparableReferenceValue>.Default.Compare(left, right) > 0;
        }

        public static bool operator >=(ComparableReferenceValue left, ComparableReferenceValue right)
        {
            return Comparer<ComparableReferenceValue>.Default.Compare(left, right) >= 0;
        }

        public static bool operator ==(ComparableReferenceValue left, ComparableReferenceValue right)
        {
            return object.Equals(left, right);
        }

        public static bool operator !=(ComparableReferenceValue left, ComparableReferenceValue right)
        {
            return !object.Equals(left, right);
        }
    }

    private enum BranchCoverageEnum : short
    {
        B = 1,
        A = 2,
        C = 3,
    }

    [Fact]
    public void TimeSpanAverage_EmptySequenceThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => Array.Empty<TimeSpan>().Average());
    }

    [Fact]
    public void TimeSpanAverage_OverflowingSumThrows()
    {
        var values = new[] { TimeSpan.MaxValue, TimeSpan.MaxValue };

        Assert.Throws<OverflowException>(() => values.Average());
    }

    [Fact]
    public void TimeSpanAverage_ComputesTheAverage()
    {
        var values = new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3) };

        Assert.Equal(TimeSpan.FromSeconds(2), values.Average());
    }

#nullable enable
    [Fact]
    public void Documentation_Filtering()
    {
        #region EnumerableExtensions_Filtering
        string?[] values = ["a", null, "", " ", "b"];

        // Remove null values, and get a sequence of non-nullable items
        IEnumerable<string> notNull = values.WhereNotNull();
        Assert.Equal(["a", "", " ", "b"], notNull);
        Assert.Equal(["a", " ", "b"], values.WhereNotNullOrEmpty());
        Assert.Equal(["a", "b"], values.WhereNotNullOrWhiteSpace());

        // Treat a null sequence as an empty one
        IEnumerable<int>? missing = null;
        Assert.Empty(missing.EmptyIfNull());
        #endregion
    }
#nullable disable

    [Fact]
    public void Documentation_Checks()
    {
        #region EnumerableExtensions_Checks
        Assert.True(new[] { 1, 2, 3 }.IsDistinct());
        Assert.False(new[] { "a", "A" }.IsDistinct(StringComparer.OrdinalIgnoreCase));
        Assert.False(new[] { (Id: 1, Name: "a"), (Id: 1, Name: "b") }.IsDistinctBy(item => item.Id));

        Assert.Equal(1, new[] { "a", "b" }.IndexOf("b", StringComparer.Ordinal));
        Assert.True(new[] { "Alpha", "Beta" }.ContainsIgnoreCase("beta"));

        Assert.Equal(TimeSpan.FromMinutes(3), new[] { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2) }.Sum());
        #endregion
    }

    [Fact]
    public void Documentation_Collections()
    {
        #region EnumerableExtensions_Collections
        ICollection<int> numbers = new List<int> { 1, 2, 3, 4 };
        numbers.AddRange(5, 6);
        numbers.RemoveAll(number => number % 2 == 0);
        Assert.Equal([1, 3, 5], numbers);

        IList<string> names = ["a", "b"];
        names.Replace("a", "A"); // Throws when the item is not in the list
        names.AddOrReplace("c", "C"); // Adds the item when it is not in the list
        Assert.Equal(["A", "b", "C"], names);
        #endregion
    }

    [Fact]
    public async Task Documentation_ForEachAsync()
    {
        #region EnumerableExtensions_ForEachAsync
        var processed = new ConcurrentBag<int>();

        // One item at a time, in order
        await new[] { 1, 2, 3 }.ForEachAsync(async item =>
        {
            await Task.Yield();
            processed.Add(item);
        });

        // Up to 2 items at the same time
        await new[] { 4, 5, 6 }.ParallelForEachAsync(degreeOfParallelism: 2, async item =>
        {
            await Task.Yield();
            processed.Add(item);
        });

        Assert.Equal([1, 2, 3, 4, 5, 6], processed.Order());
        #endregion
    }

    [Fact]
    public void Documentation_AsEnumerableOnce()
    {
        #region EnumerableExtensions_AsEnumerableOnce
        var once = new[] { 1, 2 }.AsEnumerableOnce();
        Assert.Equal(3, once.Sum());

        // A second enumeration throws, which reveals code that enumerates a sequence twice
        Assert.Throws<InvalidOperationException>(() => once.Sum());
        #endregion
    }
}
