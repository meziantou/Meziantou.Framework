using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertAllTests
{
    [Fact]
    public void Span_Success()
    {
        AssertionsAssert.All<int>([1, 2, 3], item => AssertionsAssert.True(item > 0));
    }

    [Fact]
    public void Span_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.All<int>([1, -2, 3], item => AssertionsAssert.True(item > 0)), """
            Assert.All() assertion failed: Item at index 1 failed.
            Expression:           [1, -2, 3]
            Assertion expression: item => AssertionsAssert.True(item > 0)
            Actual: [1, -̲2̲, 3]
            Exception: Assert.True() assertion failed.
                       Expression: item > 0
                       Expected: true
                       Actual:   false
            """);
    }

    [Fact]
    public void CharSpan_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.All("aBc".AsSpan(), item => AssertionsAssert.True(char.IsLower(item))), """
            Assert.All() assertion failed: Item at index 1 failed.
            Expression:           "aBc".AsSpan()
            Assertion expression: item => AssertionsAssert.True(char.IsLower(item))
            Actual: "aB̲c"
            Exception: Assert.True() assertion failed.
                       Expression: char.IsLower(item)
                       Expected: true
                       Actual:   false
            """);
    }

    [Fact]
    public void Enumerable_Success()
    {
        IEnumerable<int> actual = [1, 2, 3];

        AssertionsAssert.All(actual, item => AssertionsAssert.True(item > 0));
    }

    [Fact]
    public void Enumerable_Fails()
    {
        IEnumerable<int> actual = [1, -2, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.All(actual, item => AssertionsAssert.True(item > 0)), """
            Assert.All() assertion failed: Item at index 1 failed.
            Expression:           actual
            Assertion expression: item => AssertionsAssert.True(item > 0)
            Actual: [1, -̲2̲, 3]
            Exception: Assert.True() assertion failed.
                       Expression: item > 0
                       Expected: true
                       Actual:   false
            """);
    }

    [Fact]
    public void EnumerableIndex_Fails()
    {
        IEnumerable<int> actual = [0, 1, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.All(actual, (item, index) => AssertionsAssert.Equal(index, item)), """
            Assert.All() assertion failed: Item at index 2 failed.
            Expression:           actual
            Assertion expression: (item, index) => AssertionsAssert.Equal(index, item)
            Actual: [0, 1, 3̲]
            Exception: Assert.Equal() assertion failed.
                       Expected expression: index
                       Actual expression:   item
                       Expected: 2
                       Actual:   3
            """);
    }

    [Fact]
    public void NonGenericEnumerable_Fails()
    {
        System.Collections.IEnumerable actual = new object?[] { 1, -2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.All(actual, item => AssertionsAssert.True((int)item! > 0)), """
            Assert.All() assertion failed: Item at index 1 failed.
            Expression:           actual
            Assertion expression: item => AssertionsAssert.True((int)item! > 0)
            Actual: [1, -̲2̲, 3]
            Exception: Assert.True() assertion failed.
                       Expression: (int)item! > 0
                       Expected: true
                       Actual:   false
            """);
    }

    [Fact]
    public async Task AsyncEnumerable_Success()
    {
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);

        await AssertionsAssert.All(actual, item => AssertionsAssert.True(item > 0));
    }

    [Fact]
    public async Task AsyncEnumerable_Fails()
    {
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, -2, 3]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.All(actual, item => AssertionsAssert.True(item > 0)), """
            Assert.All() assertion failed: Item at index 1 failed.
            Expression:           actual
            Assertion expression: item => AssertionsAssert.True(item > 0)
            Actual: [1, -̲2̲, 3]
            Exception: Assert.True() assertion failed.
                       Expression: item > 0
                       Expected: true
                       Actual:   false
            """);
    }

    [Fact]
    public async Task XunitSkip_IsNotWrapped()
    {
        IEnumerable<int> actual = [1];
        System.Collections.IEnumerable nonGenericActual = new object[] { 1 };

        AssertionTestHelpers.ValidateXunitSkip(() => AssertionsAssert.All<int>([1], _ => AssertionsAssert.XunitSkip("n/a")));
        AssertionTestHelpers.ValidateXunitSkip(() => AssertionsAssert.All<int>([1], (_, _) => AssertionsAssert.XunitSkip("n/a")));
        AssertionTestHelpers.ValidateXunitSkip(() => AssertionsAssert.All(actual, _ => AssertionsAssert.XunitSkip("n/a")));
        AssertionTestHelpers.ValidateXunitSkip(() => AssertionsAssert.All(actual, (_, _) => AssertionsAssert.XunitSkip("n/a")));
        AssertionTestHelpers.ValidateXunitSkip(() => AssertionsAssert.All(nonGenericActual, _ => AssertionsAssert.XunitSkip("n/a")));
        await AssertionTestHelpers.ValidateXunitSkipAsync(() => AssertionsAssert.All(AssertionTestHelpers.ToAsyncEnumerable(actual), _ => AssertionsAssert.XunitSkip("n/a")));
        await AssertionTestHelpers.ValidateXunitSkipAsync(() => AssertionsAssert.All(AssertionTestHelpers.ToAsyncEnumerable(actual), async (_, _) =>
        {
            await Task.Yield();
            AssertionsAssert.XunitSkip("n/a");
        }));
    }

    [Fact]
    public void XunitAssertSkip_IsNotWrapped()
    {
        IEnumerable<int> actual = [1];

        var exception = AssertionsAssert.ThrowsAny<Exception>(() => AssertionsAssert.All(actual, _ => global::Xunit.Assert.Skip("n/a")));

        AssertionsAssert.IsNotType<AssertionException>(exception);
        AssertionsAssert.Equal("$XunitDynamicSkip$n/a", exception.Message);
    }

    [Fact]
    public async Task AsyncEnumerableAsyncAssertion_Fails()
    {
        var actual = AssertionTestHelpers.ToAsyncEnumerable([0, 1, 3]);
        async Task Assertion(int item, int index)
        {
            await Task.Yield();
            AssertionsAssert.Equal(index, item);
        }

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.All(actual, Assertion), """
            Assert.All() assertion failed: Item at index 2 failed.
            Expression:           actual
            Assertion expression: Assertion
            Actual: [0, 1, 3̲]
            Exception: Assert.Equal() assertion failed.
                       Expected expression: index
                       Actual expression:   item
                       Expected: 2
                       Actual:   3
            """);
    }

    [Fact]
    public async Task NullActual_Fails()
    {
        int[]? array = null;
        IEnumerable<int>? enumerable = null;
        System.Collections.IEnumerable? nonGeneric = null;
        IAsyncEnumerable<int>? asyncEnumerable = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.All(array, item => AssertionsAssert.True(item > 0)), """
            Assert.All() assertion failed.
            Expression: array
            Actual: <null>
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.All(array, item => item > 0), """
            Assert.All() assertion failed.
            Expression:           array
            Predicate expression: item => item > 0
            Actual: <null>
            """);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.All(array, (item, index) => AssertionsAssert.True(item > index)));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.All(enumerable, item => AssertionsAssert.True(item > 0)));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.All(enumerable, (item, index) => AssertionsAssert.True(item > index)));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.All(enumerable, item => item > 0));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.All(nonGeneric, item => AssertionsAssert.NotNull(item)));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.All(nonGeneric, (item, _) => AssertionsAssert.NotNull(item)));
        await AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.All(asyncEnumerable, item => AssertionsAssert.True(item > 0)));
        await AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.All(asyncEnumerable, (item, index) => AssertionsAssert.True(item > index)));
        await AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.All(asyncEnumerable, async item =>
        {
            await Task.Yield();
            AssertionsAssert.True(item > 0);
        }));
    }

    [Fact]
    public void EmptyArray_Success()
    {
        AssertionsAssert.All(Array.Empty<int>(), item => AssertionsAssert.True(item > 0));
    }

    [Fact]
    public void Predicate_Success()
    {
        IEnumerable<int> actual = [1, 2, 3];

        AssertionsAssert.All(actual, item => item > 0);
    }

    [Fact]
    public void Predicate_Fails()
    {
        IEnumerable<int> actual = [1, -2, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.All(actual, item => item > 0), """
            Assert.All() assertion failed: Item at index 1 did not satisfy the predicate.
            Expression:           actual
            Predicate expression: item => item > 0
            Actual: [1, -̲2̲, 3]
            """);
    }

    [Fact]
    public void Predicate_EndlessSequenceFailsWithoutDrainingIt()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.All(AssertionTestHelpers.EndlessSequence(), item => item < 3), """
            Assert.All() assertion failed: Item at index 3 did not satisfy the predicate.
            Expression:           AssertionTestHelpers.EndlessSequence()
            Predicate expression: item => item < 3
            Actual: [0, 1, 2, 3̲, 4, 5, 6, 7, 8, 9, ...]
            """);
    }

    [Fact]
    public void Array_LambdaReturningAValueIsAPredicate()
    {
        var seen = new HashSet<int>();

        AssertionTestHelpers.Validate(() => AssertionsAssert.All(new[] { 1, 2, 1 }, item => seen.Add(item)), """
            Assert.All() assertion failed: Item at index 2 did not satisfy the predicate.
            Expression:           new[] { 1, 2, 1 }
            Predicate expression: item => seen.Add(item)
            Actual: [1, 2, 1̲]
            """);
    }

    [Fact]
    public void Enumerable_FailureFarInALazySequenceReportsTheItemsAroundIt()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.All(AssertionTestHelpers.EndlessSequence(), item => AssertionsAssert.True(item < 5000)), """
            Assert.All() assertion failed: Item at index 5000 failed.
            Expression:           AssertionTestHelpers.EndlessSequence()
            Assertion expression: item => AssertionsAssert.True(item < 5000)
            Actual: [0, 1, 2, ..., 4998, 4999, 5̲0̲0̲0̲, 5001, 5002, ...]
            Exception: Assert.True() assertion failed.
                       Expression: item < 5000
                       Expected: true
                       Actual:   false
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.All(AssertionTestHelpers.EndlessSequence(), item => item < 5000), """
            Assert.All() assertion failed: Item at index 5000 did not satisfy the predicate.
            Expression:           AssertionTestHelpers.EndlessSequence()
            Predicate expression: item => item < 5000
            Actual: [0, 1, 2, ..., 4998, 4999, 5̲0̲0̲0̲, 5001, 5002, ...]
            """);
    }

    [Fact]
    public async Task AsyncEnumerable_FailureFarInASequenceReportsTheItemsAroundIt()
    {
        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.All(AssertionTestHelpers.ToAsyncEnumerable(AssertionTestHelpers.EndlessSequence()), item => AssertionsAssert.True(item < 5000)), """
            Assert.All() assertion failed: Item at index 5000 failed.
            Expression:           AssertionTestHelpers.ToAsyncEnumerable(AssertionTestHelpers.EndlessSequence())
            Assertion expression: item => AssertionsAssert.True(item < 5000)
            Actual: [0, 1, 2, ..., 4998, 4999, 5̲0̲0̲0̲, 5001, 5002, ...]
            Exception: Assert.True() assertion failed.
                       Expression: item < 5000
                       Expected: true
                       Actual:   false
            """);
    }

    [Fact]
    public void Enumerable_DoesNotRetainItemsTheMessageCannotShow()
    {
        var references = new List<WeakReference>();
        IEnumerable<object> Source()
        {
            for (var i = 0; i < 5_000; i++)
            {
                var item = new object();
                references.Add(new WeakReference(item));
                yield return item;
            }
        }

        var isAlive = true;
        AssertionsAssert.All(Source(), (_, index) =>
        {
            if (index == 4_000)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                isAlive = references[1_000].IsAlive;
            }
        });

        AssertionsAssert.False(isAlive);
    }

    [Fact]
    public void DoesNotAll_Success()
    {
        IEnumerable<int> actual = [1, -2, 3];

        AssertionsAssert.DoesNotAll(actual, item => item > 0);
    }

    [Fact]
    public void DoesNotAll_Fails()
    {
        IEnumerable<int> actual = [1, 2, 3];

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotAll(actual, item => item > 0), """
            Assert.DoesNotAll() assertion failed: All items satisfy the predicate, but expected at least one that does not.
            Expression:           actual
            Predicate expression: item => item > 0
            Actual: [1, 2, 3]
            """);
    }

    [Fact]
    public void DoesNotAll_EmptyCollectionFails()
    {
        IEnumerable<int> actual = [];

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotAll(actual, item => item > 0), """
            Assert.DoesNotAll() assertion failed: All items satisfy the predicate, but expected at least one that does not.
            Expression:           actual
            Predicate expression: item => item > 0
            Actual: []
            """);
    }

    [Fact]
    public void DoesNotAll_LongLazySequenceFails()
    {
        IEnumerable<int> Source()
        {
            for (var i = 0; i < 1_000; i++)
            {
                yield return i;
            }
        }

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotAll(Source(), item => item >= 0), """
            Assert.DoesNotAll() assertion failed: All items satisfy the predicate, but expected at least one that does not.
            Expression:           Source()
            Predicate expression: item => item >= 0
            Actual: [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, ...]
            """);
    }

    [Fact]
    public void DoesNotAll_EnumeratesASingleUseSequenceOnlyOnce()
    {
        var actual = AssertionTestHelpers.SingleUse(1, 2, 3);

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotAll(actual, item => item > 0), """
            Assert.DoesNotAll() assertion failed: All items satisfy the predicate, but expected at least one that does not.
            Expression:           actual
            Predicate expression: item => item > 0
            Actual: [1, 2, 3]
            """);
    }

    [Fact]
    public void DoesNotAll_StopsAtTheFirstItemThatDoesNotSatisfyThePredicate()
    {
        AssertionsAssert.DoesNotAll(AssertionTestHelpers.EndlessSequence(), item => item < 5);
    }

    [Fact]
    public void DoesNotAll_NullActual_Fails()
    {
        IEnumerable<int>? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.DoesNotAll(actual, item => item > 0), """
            Assert.DoesNotAll() assertion failed.
            Expression:           actual
            Predicate expression: item => item > 0
            Actual: <null>
            """);
    }

    public static TheoryData<int[]> PredicateCases() => new()
    {
        Array.Empty<int>(),
        new[] { 1 },
        new[] { -1 },
        new[] { 1, 2 },
        new[] { 1, -2 },
        new[] { -1, 2 },
        new[] { -1, -2 },
    };

    [Theory]
    [MemberData(nameof(PredicateCases))]
    public void AllAndDoesNotAll_AreComplements(int[] items)
    {
        static bool Predicate(int item) => item > 0;
        IEnumerable<int> Lazy()
        {
            foreach (var item in items)
            {
                yield return item;
            }
        }

        var allSatisfy = Enumerable.All(items, Predicate);
        var allException = Record.Exception(() => AssertionsAssert.All(Lazy(), Predicate));
        var doesNotAllException = Record.Exception(() => AssertionsAssert.DoesNotAll(Lazy(), Predicate));

        AssertionsAssert.Equal(allSatisfy, allException is null);
        AssertionsAssert.Equal(allSatisfy, doesNotAllException is not null);
    }
}
