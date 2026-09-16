using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertCountTests
{
    [Fact]
    public void HasCount_Success()
    {
        AssertionsAssert.HasCount<int>(3, [1, 2, 3]);
        AssertionsAssert.HasCount(3, "abc");
        AssertionsAssert.HasCount(3, new[] { 1, 2, 3 }.AsEnumerable());
    }

    [Fact]
    public void HasCount_Fails()
    {
        IEnumerable<int> actual = [1, 2, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.HasCount(2, actual), """
            Assert.HasCount() assertion failed.
            Expression: actual
            Expected count: 2
            Actual count:   3
            Actual: [1, 2, 3]
            """);
    }

    [Fact]
    public void HasCount_StringFails()
    {
        var actual = "abc";

        AssertionTestHelpers.Validate(() => AssertionsAssert.HasCount(2, actual), """
            Assert.HasCount() assertion failed.
            Expression: actual
            Expected count: 2
            Actual count:   3
            Actual: "abc"
            """);
    }

    [Fact]
    public void HasCount_NonGenericEnumerableFails()
    {
        System.Collections.IEnumerable actual = new object[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.HasCount(2, actual), """
            Assert.HasCount() assertion failed.
            Expression: actual
            Expected count: 2
            Actual count:   3
            Actual: [1, 2, 3]
            """);
    }

    [Fact]
    public async Task HasCount_AsyncEnumerableFails()
    {
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.HasCount(2, actual), """
            Assert.HasCount() assertion failed.
            Expression: actual
            Expected count: 2
            Actual count:   3
            Actual: [1, 2, 3]
            """);
    }

    [Fact]
    public void HasCountGreaterThan_Success()
    {
        AssertionsAssert.HasCountGreaterThan<int>(2, [1, 2, 3]);
        AssertionsAssert.HasCountGreaterThan(2, "abc");
        AssertionsAssert.HasCountGreaterThan(2, new[] { 1, 2, 3 }.AsEnumerable());
    }

    [Fact]
    public void HasCountGreaterThan_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.HasCountGreaterThan<int>(3, [1, 2, 3]), """
            Assert.HasCountGreaterThan() assertion failed.
            Expression: [1, 2, 3]
            Expected count: > 3
            Actual count:   3
            Actual: [1, 2, 3]
            """);
    }

    [Fact]
    public async Task HasCountGreaterThan_AsyncEnumerableFails()
    {
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.HasCountGreaterThan(3, actual), """
            Assert.HasCountGreaterThan() assertion failed.
            Expression: actual
            Expected count: > 3
            Actual count:   3
            Actual: [1, 2, 3]
            """);
    }

    [Fact]
    public void HasCountGreaterThanOrEqual_Success()
    {
        AssertionsAssert.HasCountGreaterThanOrEqual<int>(3, [1, 2, 3]);
        AssertionsAssert.HasCountGreaterThanOrEqual(3, "abc");
        AssertionsAssert.HasCountGreaterThanOrEqual(3, new[] { 1, 2, 3 }.AsEnumerable());
    }

    [Fact]
    public void HasCountGreaterThanOrEqual_Fails()
    {
        IEnumerable<int> actual = [1, 2, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.HasCountGreaterThanOrEqual(4, actual), """
            Assert.HasCountGreaterThanOrEqual() assertion failed.
            Expression: actual
            Expected count: >= 4
            Actual count:   3
            Actual: [1, 2, 3]
            """);
    }

    [Fact]
    public void HasCountLessThan_Success()
    {
        AssertionsAssert.HasCountLessThan<int>(4, [1, 2, 3]);
        AssertionsAssert.HasCountLessThan(4, "abc");
        AssertionsAssert.HasCountLessThan(4, new[] { 1, 2, 3 }.AsEnumerable());
    }

    [Fact]
    public void HasCountLessThan_Fails()
    {
        System.Collections.IEnumerable actual = new object[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.HasCountLessThan(3, actual), """
            Assert.HasCountLessThan() assertion failed.
            Expression: actual
            Expected count: < 3
            Actual count:   3
            Actual: [1, 2, 3]
            """);
    }

    [Fact]
    public void HasCountLessThanOrEqual_Success()
    {
        AssertionsAssert.HasCountLessThanOrEqual<int>(3, [1, 2, 3]);
        AssertionsAssert.HasCountLessThanOrEqual(3, "abc");
        AssertionsAssert.HasCountLessThanOrEqual(3, new[] { 1, 2, 3 }.AsEnumerable());
    }

    [Fact]
    public void HasCountLessThanOrEqual_Fails()
    {
        IEnumerable<int> actual = [1, 2, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.HasCountLessThanOrEqual(2, actual), """
            Assert.HasCountLessThanOrEqual() assertion failed.
            Expression: actual
            Expected count: <= 2
            Actual count:   3
            Actual: [1, 2, 3]
            """);
    }

    [Fact]
    public void DoesNotHaveCount_Success()
    {
        AssertionsAssert.DoesNotHaveCount<int>(2, [1, 2, 3]);
        AssertionsAssert.DoesNotHaveCount(2, "abc");
    }

    [Fact]
    public void DoesNotHaveCount_Fails()
    {
        var actual = new[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotHaveCount(3, actual), """
            Assert.DoesNotHaveCount() assertion failed.
            Expression: actual
            Not expected count: 3
            Actual count:       3
            Actual: [1, 2, 3]
            """);
    }

    [Fact]
    public void HasCountGreaterThan_DoesNotEnumerateInfiniteSequence()
    {
        AssertionsAssert.HasCountGreaterThan(3, InfiniteSequence());
    }

    [Fact]
    public void HasCountGreaterThanOrEqual_StopsEnumeratingAtExpectedCount()
    {
        var enumerated = 0;

        AssertionsAssert.HasCountGreaterThanOrEqual(3, CountingSequence(10, () => enumerated++));

        AssertionsAssert.Equal(3, enumerated);
    }

    [Fact]
    public void HasCount_UsesCountOfReadOnlyCollectionWithoutEnumerating()
    {
        var enumerated = 0;
        var actual = new CountingCollection(3, () => enumerated++);

        AssertionsAssert.HasCount(3, actual);

        AssertionsAssert.Equal(0, enumerated);
    }

    [Fact]
    public void HasCount_UsesCountOfCollectionWithoutEnumerating()
    {
        // A HashSet<T> is an ICollection<T> but not an IList<T>, so it used to be copied item by item.
        IEnumerable<int> actual = new HashSet<int> { 1, 2, 3 };

        AssertionsAssert.HasCount(3, actual);
        AssertionsAssert.HasCountGreaterThan(2, actual);
        AssertionsAssert.HasCountLessThan(4, actual);
    }

    private static IEnumerable<int> InfiniteSequence()
    {
        for (var i = 0; ; i++)
        {
            yield return i;
        }
    }

    private static IEnumerable<int> CountingSequence(int count, Action onItem)
    {
        for (var i = 0; i < count; i++)
        {
            onItem();
            yield return i;
        }
    }

    private sealed class CountingCollection(int count, Action onItem) : IReadOnlyCollection<int>
    {
        public int Count => count;

        public IEnumerator<int> GetEnumerator()
        {
            for (var i = 0; i < count; i++)
            {
                onItem();
                yield return i;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public async Task DoesNotHaveCount_DoesNotDrainEndlessSequence()
    {
        AssertionsAssert.DoesNotHaveCount(3, AssertionTestHelpers.EndlessSequence());
        AssertionsAssert.DoesNotHaveCount(3, (System.Collections.IEnumerable)AssertionTestHelpers.EndlessSequence());
        await AssertionsAssert.DoesNotHaveCount(3, AssertionTestHelpers.ToAsyncEnumerable(AssertionTestHelpers.EndlessSequence()));
    }

    [Fact]
    public void DoesNotHaveCount_StopsEnumeratingAfterExpectedCountPlusOne()
    {
        var enumerated = 0;

        AssertionsAssert.DoesNotHaveCount(3, CountingSequence(10, () => enumerated++));

        AssertionsAssert.Equal(4, enumerated);
    }

    [Fact]
    public void DoesNotHaveCount_UsesCountOfReadOnlyCollectionWithoutEnumerating()
    {
        var enumerated = 0;
        var actual = new CountingCollection(3, () => enumerated++);

        AssertionsAssert.DoesNotHaveCount(2, actual);

        AssertionsAssert.Equal(0, enumerated);
    }

    [Fact]
    public void DoesNotHaveCount_FailsWhenSequenceHasExpectedCount()
    {
        var actual = CountingSequence(3, () => { });

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotHaveCount(3, actual), """
            Assert.DoesNotHaveCount() assertion failed.
            Expression: actual
            Not expected count: 3
            Actual count:       3
            Actual: [0, 1, 2]
            """);
    }

    [Fact]
    public void DoesNotHaveCount_EnumeratesASingleUseSequenceOnlyOnce()
    {
        var actual = AssertionTestHelpers.SingleUse(1, 2, 3);

        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.DoesNotHaveCount(3, actual));
    }

    [Fact]
    public async Task HasCount_NullActual_Fails()
    {
        int[]? array = null;
        string? text = null;
        IEnumerable<int>? enumerable = null;
        System.Collections.IEnumerable? nonGeneric = null;
        IAsyncEnumerable<int>? asyncEnumerable = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.HasCount(3, array), """
            Assert.HasCount() assertion failed.
            Expression: array
            Expected count: 3
            Actual:         <null>
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.HasCountGreaterThan(3, text), """
            Assert.HasCountGreaterThan() assertion failed.
            Expression: text
            Expected count: > 3
            Actual:         <null>
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.HasCountGreaterThanOrEqual(3, enumerable), """
            Assert.HasCountGreaterThanOrEqual() assertion failed.
            Expression: enumerable
            Expected count: >= 3
            Actual:         <null>
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.HasCountLessThan(3, nonGeneric), """
            Assert.HasCountLessThan() assertion failed.
            Expression: nonGeneric
            Expected count: < 3
            Actual:         <null>
            """);
        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.HasCountLessThanOrEqual(3, asyncEnumerable), """
            Assert.HasCountLessThanOrEqual() assertion failed.
            Expression: asyncEnumerable
            Expected count: <= 3
            Actual:         <null>
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotHaveCount(3, array), """
            Assert.DoesNotHaveCount() assertion failed.
            Expression: array
            Not expected count: 3
            Actual:             <null>
            """);
    }

    [Fact]
    public async Task CountAssertions_NullActual_FailForEveryOverload()
    {
        int[]? array = null;
        string? text = null;
        IEnumerable<int>? enumerable = null;
        System.Collections.IEnumerable? nonGeneric = null;
        IAsyncEnumerable<int>? asyncEnumerable = null;

        foreach (var assertion in new Action[]
        {
            () => AssertionsAssert.HasCount(3, text),
            () => AssertionsAssert.HasCount(3, enumerable),
            () => AssertionsAssert.HasCount(3, nonGeneric),
            () => AssertionsAssert.HasCountGreaterThan(3, array),
            () => AssertionsAssert.HasCountGreaterThan(3, enumerable),
            () => AssertionsAssert.HasCountGreaterThan(3, nonGeneric),
            () => AssertionsAssert.HasCountGreaterThanOrEqual(3, array),
            () => AssertionsAssert.HasCountGreaterThanOrEqual(3, text),
            () => AssertionsAssert.HasCountGreaterThanOrEqual(3, nonGeneric),
            () => AssertionsAssert.HasCountLessThan(3, array),
            () => AssertionsAssert.HasCountLessThan(3, text),
            () => AssertionsAssert.HasCountLessThan(3, enumerable),
            () => AssertionsAssert.HasCountLessThanOrEqual(3, array),
            () => AssertionsAssert.HasCountLessThanOrEqual(3, text),
            () => AssertionsAssert.HasCountLessThanOrEqual(3, enumerable),
            () => AssertionsAssert.HasCountLessThanOrEqual(3, nonGeneric),
            () => AssertionsAssert.DoesNotHaveCount(3, text),
            () => AssertionsAssert.DoesNotHaveCount(3, enumerable),
            () => AssertionsAssert.DoesNotHaveCount(3, nonGeneric),
        })
        {
            AssertionsAssert.Throws<AssertionException>(assertion);
        }

        foreach (var assertion in new Func<Task>[]
        {
            () => AssertionsAssert.HasCount(3, asyncEnumerable),
            () => AssertionsAssert.HasCountGreaterThan(3, asyncEnumerable),
            () => AssertionsAssert.HasCountGreaterThanOrEqual(3, asyncEnumerable),
            () => AssertionsAssert.HasCountLessThan(3, asyncEnumerable),
            () => AssertionsAssert.DoesNotHaveCount(3, asyncEnumerable),
        })
        {
            await AssertionsAssert.Throws<AssertionException>(assertion);
        }
    }

    [Fact]
    public void HasCount_EmptyArrayIsNotNull()
    {
        var actual = Array.Empty<int>();

        AssertionsAssert.HasCountLessThan(3, actual);
        AssertionsAssert.DoesNotHaveCount(3, actual);
    }

    [Fact]
    public async Task HasCount_EndlessSequenceFailsWithoutDrainingIt()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.HasCount(3, AssertionTestHelpers.EndlessSequence()), """
            Assert.HasCount() assertion failed.
            Expression: AssertionTestHelpers.EndlessSequence()
            Expected count: 3
            Actual count:   at least 11
            Actual: [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, ...]
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.HasCount(3, (System.Collections.IEnumerable)AssertionTestHelpers.EndlessSequence()), """
            Assert.HasCount() assertion failed.
            Expression: (System.Collections.IEnumerable)AssertionTestHelpers.EndlessSequence()
            Expected count: 3
            Actual count:   at least 11
            Actual: [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, ...]
            """);
        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.HasCount(3, AssertionTestHelpers.ToAsyncEnumerable(AssertionTestHelpers.EndlessSequence())), """
            Assert.HasCount() assertion failed.
            Expression: AssertionTestHelpers.ToAsyncEnumerable(AssertionTestHelpers.EndlessSequence())
            Expected count: 3
            Actual count:   at least 11
            Actual: [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, ...]
            """);
    }

    [Fact]
    public async Task HasCountLessThan_EndlessSequenceFailsWithoutDrainingIt()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.HasCountLessThan(3, AssertionTestHelpers.EndlessSequence()), """
            Assert.HasCountLessThan() assertion failed.
            Expression: AssertionTestHelpers.EndlessSequence()
            Expected count: < 3
            Actual count:   at least 11
            Actual: [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, ...]
            """);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.HasCountLessThan(3, (System.Collections.IEnumerable)AssertionTestHelpers.EndlessSequence()));
        await AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.HasCountLessThan(3, AssertionTestHelpers.ToAsyncEnumerable(AssertionTestHelpers.EndlessSequence())));
    }

    [Fact]
    public async Task HasCountLessThanOrEqual_EndlessSequenceFailsWithoutDrainingIt()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.HasCountLessThanOrEqual(30, AssertionTestHelpers.EndlessSequence()), """
            Assert.HasCountLessThanOrEqual() assertion failed.
            Expression: AssertionTestHelpers.EndlessSequence()
            Expected count: <= 30
            Actual count:   at least 31
            Actual: [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, ...]
            """);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.HasCountLessThanOrEqual(3, (System.Collections.IEnumerable)AssertionTestHelpers.EndlessSequence()));
        await AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.HasCountLessThanOrEqual(3, AssertionTestHelpers.ToAsyncEnumerable(AssertionTestHelpers.EndlessSequence())));
    }

    [Fact]
    public void HasCount_LazySequenceShorterThanTheMessageReportsItsExactCount()
    {
        var actual = CountingSequence(10, () => { });

        AssertionTestHelpers.Validate(() => AssertionsAssert.HasCount(3, actual), """
            Assert.HasCount() assertion failed.
            Expression: actual
            Expected count: 3
            Actual count:   10
            Actual: [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
            """);
    }

    public static TheoryData<string, int> CountComparisonCases()
    {
        var data = new TheoryData<string, int>();
        foreach (var assertion in new[] { nameof(AssertionsAssert.HasCount), nameof(AssertionsAssert.DoesNotHaveCount), nameof(AssertionsAssert.HasCountGreaterThan), nameof(AssertionsAssert.HasCountGreaterThanOrEqual), nameof(AssertionsAssert.HasCountLessThan), nameof(AssertionsAssert.HasCountLessThanOrEqual) })
        {
            foreach (var delta in new[] { -1, 0, 1 })
            {
                data.Add(assertion, delta);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CountComparisonCases))]
    public async Task CountComparison_LazySequences_MatchIntegerComparison(string assertion, int delta)
    {
        const int ExpectedCount = 3;
        var actualCount = ExpectedCount + delta;
        var shouldPass = assertion switch
        {
            nameof(AssertionsAssert.HasCount) => actualCount == ExpectedCount,
            nameof(AssertionsAssert.DoesNotHaveCount) => actualCount != ExpectedCount,
            nameof(AssertionsAssert.HasCountGreaterThan) => actualCount > ExpectedCount,
            nameof(AssertionsAssert.HasCountGreaterThanOrEqual) => actualCount >= ExpectedCount,
            nameof(AssertionsAssert.HasCountLessThan) => actualCount < ExpectedCount,
            nameof(AssertionsAssert.HasCountLessThanOrEqual) => actualCount <= ExpectedCount,
            _ => throw new ArgumentOutOfRangeException(nameof(assertion)),
        };

        IEnumerable<int> Lazy() => CountingSequence(actualCount, () => { });

        Action sync = assertion switch
        {
            nameof(AssertionsAssert.HasCount) => () => AssertionsAssert.HasCount(ExpectedCount, Lazy()),
            nameof(AssertionsAssert.DoesNotHaveCount) => () => AssertionsAssert.DoesNotHaveCount(ExpectedCount, Lazy()),
            nameof(AssertionsAssert.HasCountGreaterThan) => () => AssertionsAssert.HasCountGreaterThan(ExpectedCount, Lazy()),
            nameof(AssertionsAssert.HasCountGreaterThanOrEqual) => () => AssertionsAssert.HasCountGreaterThanOrEqual(ExpectedCount, Lazy()),
            nameof(AssertionsAssert.HasCountLessThan) => () => AssertionsAssert.HasCountLessThan(ExpectedCount, Lazy()),
            _ => () => AssertionsAssert.HasCountLessThanOrEqual(ExpectedCount, Lazy()),
        };

        Action nonGeneric = assertion switch
        {
            nameof(AssertionsAssert.HasCount) => () => AssertionsAssert.HasCount(ExpectedCount, (System.Collections.IEnumerable)Lazy()),
            nameof(AssertionsAssert.DoesNotHaveCount) => () => AssertionsAssert.DoesNotHaveCount(ExpectedCount, (System.Collections.IEnumerable)Lazy()),
            nameof(AssertionsAssert.HasCountGreaterThan) => () => AssertionsAssert.HasCountGreaterThan(ExpectedCount, (System.Collections.IEnumerable)Lazy()),
            nameof(AssertionsAssert.HasCountGreaterThanOrEqual) => () => AssertionsAssert.HasCountGreaterThanOrEqual(ExpectedCount, (System.Collections.IEnumerable)Lazy()),
            nameof(AssertionsAssert.HasCountLessThan) => () => AssertionsAssert.HasCountLessThan(ExpectedCount, (System.Collections.IEnumerable)Lazy()),
            _ => () => AssertionsAssert.HasCountLessThanOrEqual(ExpectedCount, (System.Collections.IEnumerable)Lazy()),
        };

        Func<Task> async = assertion switch
        {
            nameof(AssertionsAssert.HasCount) => () => AssertionsAssert.HasCount(ExpectedCount, AssertionTestHelpers.ToAsyncEnumerable(Lazy())),
            nameof(AssertionsAssert.DoesNotHaveCount) => () => AssertionsAssert.DoesNotHaveCount(ExpectedCount, AssertionTestHelpers.ToAsyncEnumerable(Lazy())),
            nameof(AssertionsAssert.HasCountGreaterThan) => () => AssertionsAssert.HasCountGreaterThan(ExpectedCount, AssertionTestHelpers.ToAsyncEnumerable(Lazy())),
            nameof(AssertionsAssert.HasCountGreaterThanOrEqual) => () => AssertionsAssert.HasCountGreaterThanOrEqual(ExpectedCount, AssertionTestHelpers.ToAsyncEnumerable(Lazy())),
            nameof(AssertionsAssert.HasCountLessThan) => () => AssertionsAssert.HasCountLessThan(ExpectedCount, AssertionTestHelpers.ToAsyncEnumerable(Lazy())),
            _ => () => AssertionsAssert.HasCountLessThanOrEqual(ExpectedCount, AssertionTestHelpers.ToAsyncEnumerable(Lazy())),
        };

        if (shouldPass)
        {
            sync();
            nonGeneric();
            await async();
        }
        else
        {
            AssertionsAssert.Throws<AssertionException>(sync);
            AssertionsAssert.Throws<AssertionException>(nonGeneric);
            await AssertionsAssert.Throws<AssertionException>(async);
        }
    }

    [Fact]
    public async Task HasCountGreaterThanOrEqual_AsyncEnumerableFails()
    {
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.HasCountGreaterThanOrEqual(4, actual), """
            Assert.HasCountGreaterThanOrEqual() assertion failed.
            Expression: actual
            Expected count: >= 4
            Actual count:   3
            Actual: [1, 2, 3]
            """);
    }

    [Fact]
    public async Task HasCountLessThan_AsyncEnumerableFails()
    {
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.HasCountLessThan(3, actual), """
            Assert.HasCountLessThan() assertion failed.
            Expression: actual
            Expected count: < 3
            Actual count:   3
            Actual: [1, 2, 3]
            """);
    }

    [Fact]
    public async Task HasCountLessThanOrEqual_AsyncEnumerableFails()
    {
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.HasCountLessThanOrEqual(2, actual), """
            Assert.HasCountLessThanOrEqual() assertion failed.
            Expression: actual
            Expected count: <= 2
            Actual count:   3
            Actual: [1, 2, 3]
            """);
    }

    [Fact]
    public async Task DoesNotHaveCount_AsyncEnumerableFails()
    {
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.DoesNotHaveCount(3, actual), """
            Assert.DoesNotHaveCount() assertion failed.
            Expression: actual
            Not expected count: 3
            Actual count:       3
            Actual: [1, 2, 3]
            """);
    }
}
