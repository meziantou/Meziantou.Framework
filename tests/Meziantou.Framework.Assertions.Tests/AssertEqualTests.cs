using System.Collections.Immutable;
using System.Runtime.InteropServices;
using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertEqualTests
{
    [Fact]
    public void DifferentNumericTypes_Success()
    {
        AssertionsAssert.Equal(42, 42L);
        AssertionsAssert.Equal(42u, 42);
        AssertionsAssert.Equal(42m, 42);
        AssertionsAssert.Equal(1.5f, 1.5d);

        object expected = 123;
        object actual = 123L;

        AssertionsAssert.Equal(expected, actual);
    }

    [Fact]
    public void DifferentNumericTypes_Fails()
    {
        var expected = 42;
        var actual = 43L;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual), """
            Assert.Equal() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: 42
            Actual:   43
            """);
    }

    [Fact]
    public void Scalar_FailsWhenActualIsNull()
    {
        object expected = 42;
        object? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual), """
            Assert.Equal() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: 42
            Actual:   <null>
            """);
    }

    [Fact]
    public void Scalar_SucceedsWhenBothValuesAreNull()
    {
        Type? expected = null;
        Type? actual = null;

        AssertionsAssert.Equal(expected, actual);
    }

    [Fact]
    public void Scalar_SucceedsWithDynamicActual()
    {
        dynamic actual = 42;

        AssertionsAssert.Equal(42, actual);
    }

    [Fact]
    public void Scalar_SucceedsWhenActualIsNativeInteger()
    {
        nint actual = 42;

        AssertionsAssert.Equal(42, actual);
    }

    [Fact]
    public void Scalar_SucceedsWhenExpectedConvertsToActualType()
    {
        var actual = new ImplicitlyConvertibleValue(42);

        AssertionsAssert.Equal(42, actual);
    }

    [Fact]
    public void Scalar_SucceedsWhenExpectedConvertsToActualTypeRepeatedly()
    {
        var actual = new ImplicitlyConvertibleValue(42);

        AssertionsAssert.Equal(42, actual);
        AssertionsAssert.Equal(42, actual);
    }

    [Fact]
    public void HalfTolerance_Success()
    {
        Half expected = (Half)1;
        Half actual = (Half)1.1;
        Half tolerance = (Half)0.2;

        AssertionsAssert.Equal(expected, actual, tolerance);
    }

    [Fact]
    public void SingleTolerance_Success()
    {
        var expected = 1f;
        var actual = 1.1f;
        var tolerance = 0.2f;

        AssertionsAssert.Equal(expected, actual, tolerance);
    }

    [Fact]
    public void DoubleTolerance_Success()
    {
        var expected = 1d;
        var actual = 1.1d;
        var tolerance = 0.2d;

        AssertionsAssert.Equal(expected, actual, tolerance);
    }

    [Fact]
    public void DecimalTolerance_Success()
    {
        var expected = 1m;
        var actual = 1.1m;
        var tolerance = 0.2m;

        AssertionsAssert.Equal(expected, actual, tolerance);
    }

    [Fact]
    public void DecimalTolerance_ValuesTooFarApartToSubtract()
    {
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal(decimal.MaxValue, -1m, 1m));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal(decimal.MinValue, decimal.MaxValue, decimal.MaxValue));
        AssertionsAssert.NotEqual(decimal.MaxValue, decimal.MinValue, 1m);
        AssertionsAssert.NotEqual(-1m, decimal.MaxValue, decimal.MaxValue);
    }

    [Fact]
    public void DecimalTolerance_ValuesOfOppositeSigns()
    {
        AssertionsAssert.Equal(0.5m, -0.5m, 1m);
        AssertionsAssert.Equal(-0.25m, 0.5m, 1m);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal(0.5m, -0.75m, 1m));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(0.5m, -0.5m, 1m));
        AssertionsAssert.NotEqual(0.5m, -0.75m, 1m);
    }

    [Fact]
    public void NFloatTolerance_Success()
    {
        var expected = (NFloat)1;
        var actual = (NFloat)1.1;
        var tolerance = (NFloat)0.2;

        AssertionsAssert.Equal(expected, actual, tolerance);
    }

    [Fact]
    public void NFloatTolerance_Fails()
    {
        var expected = (NFloat)1;
        var actual = (NFloat)1.3;
        var tolerance = (NFloat)0.2;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual, tolerance), """
            Assert.Equal() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: 1
            Actual:   1.3
            Tolerance: 0.2
            """);
    }

    [Fact]
    public void NFloatTolerance_AllowsSameSpecialValues()
    {
        AssertionsAssert.Equal(NFloat.NaN, NFloat.NaN, (NFloat)0);
        AssertionsAssert.Equal(NFloat.PositiveInfinity, NFloat.PositiveInfinity, (NFloat)0);
    }

    [Fact]
    public void SingleTolerance_AllowsSameSpecialValues()
    {
        AssertionsAssert.Equal(float.NaN, float.NaN, 0f);
        AssertionsAssert.Equal(float.PositiveInfinity, float.PositiveInfinity, 0f);
    }

    [Fact]
    public void SingleTolerance_Fails()
    {
        var expected = 1f;
        var actual = 1.3f;
        var tolerance = 0.2f;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual, tolerance), """
            Assert.Equal() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: 1
            Actual:   1.3
            Tolerance: 0.2
            """);
    }

    [Fact]
    public void DoubleTolerance_FailsWithMessage()
    {
        var expected = 1d;
        var actual = 1.3d;
        var tolerance = 0.2d;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual, tolerance, "custom message"), """
            Assert.Equal() assertion failed.
            Message: custom message
            Expected expression: expected
            Actual expression:   actual
            Expected: 1
            Actual:   1.3
            Tolerance: 0.2
            """);
    }

    [Fact]
    public void DifferentScalarTypes_Success()
    {
        var dateTime = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var dateTimeOffset = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);

        AssertionsAssert.Equal(dateTime, (object)dateTime);
        AssertionsAssert.Equal(dateTimeOffset, (object)dateTimeOffset);
        AssertionsAssert.Equal('a', (object)'a');
        AssertionsAssert.Equal("abc", (object)"abc");
    }

    [Fact]
    public void EscapesStringValues()
    {
        var expected = "Hello\n\"World\"";
        var actual = "Hello\tWorld";

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual), """
            Assert.Equal() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Index of first difference: 5
            Expected: "Hello\̲n̲\"World\""
            Actual:   "Hello\̲t̲World"
            """);
    }

    [Fact]
    public void String_FailsWhenActualIsNull()
    {
        var expected = "Hello";
        string? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual), """
            Assert.Equal() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: "Hello"
            Actual:   <null>
            """);
    }

    [Fact]
    public void String_SucceedsWhenBothValuesAreNull()
    {
        string? expected = null;
        string? actual = null;

        AssertionsAssert.Equal(expected, actual);
    }

    [Fact]
    public void String_IgnoreCase_Success()
    {
        AssertionsAssert.Equal("Hello", "hello", ignoreCase: true);
    }

    [Fact]
    public void String_IgnoreCase_FailsWhenDisabled()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal("Hello", "hello"), """
            Assert.Equal() assertion failed.
            Expected expression: "Hello"
            Actual expression:   "hello"
            Index of first difference: 0
            Expected: "H̲ello"
            Actual:   "h̲ello"
            """);
    }

    [Fact]
    public void String_IgnoreCase_FailsAtFirstRelevantDifference()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal("HelloX", "helloY", ignoreCase: true), """
            Assert.Equal() assertion failed.
            Expected expression: "HelloX"
            Actual expression:   "helloY"
            Index of first difference: 5
            Expected: "HelloX̲"
            Actual:   "helloY̲"
            """);
    }

    [Fact]
    public void BoxedString_FailsWithFirstDifference()
    {
        object expected = "abc";
        object actual = "axc";

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual), """
            Assert.Equal() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Index of first difference: 1
            Expected: "ab̲c"
            Actual:   "ax̲c"
            """);
    }

    [Fact]
    public void IgnoreLineEndingDifferencesWithoutCarriageReturn_Success()
    {
        var expected = "line1\nline2";
        var actual = "line1\nline2";

        AssertionsAssert.Equal(expected, actual, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void IgnoreLineEndingDifferencesWithoutCarriageReturn_Fails()
    {
        var expected = "line1\nline2";
        var actual = "line1\nline3";

        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal(expected, actual, ignoreLineEndingDifferences: true));
    }

    [Fact]
    public void String_IgnoreLineEndingDifferences_Success()
    {
        var expected = "line1\r\nline2\rline3";
        var actual = "line1\nline2\nline3";

        AssertionsAssert.Equal(expected, actual, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void String_IgnoreCaseAndLineEndingDifferences_Success()
    {
        var expected = "line1\r\nline2\rline3";
        var actual = "LINE1\nLINE2\nLINE3";

        AssertionsAssert.Equal(expected, actual, ignoreCase: true, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void String_IgnoreLineEndingDifferences_FailsWhenDisabled()
    {
        var expected = "line1\r\nline2";
        var actual = "line1\nline2";

        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal(expected, actual, ignoreLineEndingDifferences: false));
    }

    [Fact]
    public void CharSpan_IgnoreLineEndingDifferences_Success()
    {
        var expected = "line1\r\nline2\rline3";
        var actual = "line1\nline2\nline3";

        AssertionsAssert.Equal(expected.AsSpan(), actual.AsSpan(), ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void CharSpan_IgnoreCase_Success()
    {
        AssertionsAssert.Equal("Hello".AsSpan(), "hello".AsSpan(), ignoreCase: true);
    }

    [Fact]
    public void CharSpan_IgnoreLineEndingDifferences_FailsWhenOtherDifferences()
    {
        var expected = "line1\r\nline2";
        var actual = "line1\nlineX";

        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal(expected.AsSpan(), actual.AsSpan(), ignoreLineEndingDifferences: true));
    }

    [Fact]
    public void DifferentEnumerableTypes_Success()
    {
        IEnumerable<int> expected = [1, 2, 3];
        IEnumerable<long> actual = [1L, 2L, 3L];

        AssertionsAssert.Equal(expected, actual);
    }

    [Fact]
    public void DifferentNonGenericEnumerableTypes_Success()
    {
        System.Collections.IEnumerable expected = new object[] { 1, "a", 3 };
        System.Collections.IEnumerable actual = new object[] { 1L, "a", 3L };

        AssertionsAssert.Equal(expected, actual);
    }

    [Fact]
    public void DifferentCollectionTypes_Success()
    {
        ICollection<int> expected = [1, 2, 3];
        ICollection<long> actual = [1L, 2L, 3L];

        AssertionsAssert.Equal(expected, actual);
    }

    [Fact]
    public void DifferentNonGenericCollectionTypes_Success()
    {
        System.Collections.ICollection expected = new object[] { 1, 2, 3 };
        System.Collections.ICollection actual = new object[] { 1L, 2L, 3L };

        AssertionsAssert.Equal(expected, actual);
    }

    [Fact]
    public void DifferentReadOnlyCollectionTypes_Success()
    {
        IReadOnlyCollection<int> expected = [1, 2, 3];
        IReadOnlyCollection<long> actual = [1L, 2L, 3L];

        AssertionsAssert.Equal(expected, actual);
    }

    [Fact]
    public void DifferentImmutableArrayTypes_Success()
    {
        var expected = ImmutableArray.Create(1, 2, 3);
        var actual = ImmutableArray.Create(1L, 2L, 3L);

        AssertionsAssert.Equal(expected, actual);
    }

    [Fact]
    public void SameImmutableArrayTypes_Success()
    {
        var expected = ImmutableArray.Create(1, 2, 3);
        var actual = ImmutableArray.Create(1, 2, 3);

        AssertionsAssert.Equal(expected, actual);
    }

    [Fact]
    public void NestedSameImmutableArrayTypes_Success()
    {
        ImmutableArray<int>[] expected = [ImmutableArray.Create(1, 2), ImmutableArray.Create(3)];
        ImmutableArray<int>[] actual = [ImmutableArray.Create(1, 2), ImmutableArray.Create(3)];

        AssertionsAssert.Equal(expected, actual);
    }

    [Fact]
    public void NestedSameImmutableArrayTypes_Fails()
    {
        ImmutableArray<int>[] expected = [ImmutableArray.Create(1, 2)];
        ImmutableArray<int>[] actual = [ImmutableArray.Create(1, 3)];

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual), """
            Assert.Equal() assertion failed: Item at index 0 differs.
            Expected expression: expected
            Actual expression:   actual
            Index of first difference: 0
            Expected item: [[̲1̲,̲ ̲2̲]̲]
            Actual item:   [[̲1̲,̲ ̲3̲]̲]
            """);
    }

    [Fact]
    public void DifferentListTypes_Success()
    {
        var expected = new List<int> { 1, 2, 3 };
        var actual = new List<long> { 1L, 2L, 3L };

        AssertionsAssert.Equal(expected, actual);
    }

    [Fact]
    public void JaggedArrays_Success()
    {
        string[][] expected =
        [
            ["a", "b"],
            ["c", "d"],
        ];
        string[][] actual =
        [
            ["a", "b"],
            ["c", "d"],
        ];

        AssertionsAssert.Equal(expected, actual);
    }

    [Fact]
    public void ListsOfArrays_CompareNestedContent()
    {
        var expected = new List<int[]> { new[] { 1, 2 } };
        var actual = new List<int[]> { new[] { 1, 2 } };
        IEnumerable<int[]> expectedEnumerable = expected;
        IEnumerable<int[]> actualEnumerable = actual;

        AssertionsAssert.Equal(expected, actual);
        AssertionsAssert.Equal(expectedEnumerable, actualEnumerable);
        AssertionsAssert.Equal(expectedEnumerable, actualEnumerable, comparer: (IEqualityComparer<int[]>?)null);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(expected, actual));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(expectedEnumerable, actualEnumerable));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(expectedEnumerable, actualEnumerable, comparer: (IEqualityComparer<int[]>?)null));
    }

    [Fact]
    public void ListsOfArrays_ExplicitDefaultComparerComparesReferences()
    {
        IEnumerable<int[]> expected = new List<int[]> { new[] { 1, 2 } };
        IEnumerable<int[]> actual = new List<int[]> { new[] { 1, 2 } };

        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal(expected, actual, EqualityComparer<int[]>.Default));
        AssertionsAssert.NotEqual(expected, actual, EqualityComparer<int[]>.Default);
    }

    [Fact]
    public void ListsOfBoxedNumbers_CompareValues()
    {
        var expected = new List<object> { 1 };
        var actual = new List<object> { 1L };

        AssertionsAssert.Equal(expected, actual);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(expected, actual));
    }

    [Fact]
    public void ListsOfArrays_FailWhenNestedContentDiffers()
    {
        var expected = new List<int[]> { new[] { 1, 2 } };
        var actual = new List<int[]> { new[] { 1, 3 } };

        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal(expected, actual));
        AssertionsAssert.NotEqual(expected, actual);
    }

    [Fact]
    public async Task AsyncEnumerablesOfArrays_CompareNestedContent()
    {
        var expected = AssertionTestHelpers.ToAsyncEnumerable([new[] { 1 }, new[] { 2 }]);
        var actual = AssertionTestHelpers.ToAsyncEnumerable([new[] { 1 }, new[] { 2 }]);

        await AssertionsAssert.Equal(expected, actual);
        await AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(expected, actual));
    }

    [Fact]
    public void NullArray_IsNotEqualToEmptyArray()
    {
        int[]? expected = null;
        var actual = Array.Empty<int>();

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual), """
            Assert.Equal() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: <null>
            Actual:   []
            """);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal(actual, expected));
        AssertionsAssert.NotEqual(expected, actual);
        AssertionsAssert.NotEqual(actual, expected);
    }

    [Fact]
    public void NullArrays_AreEqual()
    {
        int[]? expected = null;
        int[]? actual = null;

        AssertionsAssert.Equal(expected, actual);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(expected, actual));
    }

    [Fact]
    public void Arrays_KeepReadOnlySpanFailureMessage()
    {
        var expected = new[] { 1, 2 };
        var actual = new[] { 1 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual), """
            Assert.Equal() assertion failed: Lengths differ.
            Expected expression: expected
            Actual expression:   actual
            Expected length: 2
            Actual length:   1
            Index of first difference: 1
            Expected: [1, 2̲]
            Actual:   [1]
            """);
    }

    [Fact]
    public void ArrayAndCollectionExpression_Success()
    {
        var actual = new[] { 1, 2 };

        AssertionsAssert.Equal([1, 2], actual);
    }

    [Fact]
    public void DifferentConcreteCollectionTypes_Fails()
    {
        var expected = new List<int> { 1, 2, 3 };
        var actual = new List<long> { 1L, 42L, 3L };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual), """
            Assert.Equal() assertion failed: Item at index 1 differs.
            Expected expression: expected
            Actual expression:   actual
            Index of first difference: 1
            Expected: [1, 2̲, 3]
            Actual:   [1, 4̲2̲, 3]
            """);
    }

    [Fact]
    public void DifferentReadOnlySpanTypes_Success()
    {
        ReadOnlySpan<int> expected = [1, 2, 3];
        ReadOnlySpan<long> actual = [1L, 2L, 3L];

        AssertionsAssert.Equal(expected, actual);
    }

    [Fact]
    public void DifferentSpanTypes_Success()
    {
        Span<int> expected = [1, 2, 3];
        Span<long> actual = [1L, 2L, 3L];

        AssertionsAssert.Equal(expected, actual);
    }

    [Fact]
    public void DifferentMemoryTypes_Success()
    {
        var expected = new[] { 1, 2, 3 }.AsMemory();
        var actual = new[] { 1L, 2L, 3L }.AsMemory();

        AssertionsAssert.Equal(expected, actual);
    }

    [Fact]
    public void DifferentMemoryTypes_BoxedValuesSuccessRepeatedly()
    {
        object expected = new[] { 1, 2, 3 }.AsMemory();
        object actual = new[] { 1L, 2L, 3L }.AsMemory();

        AssertionsAssert.Equal(expected, actual);
        AssertionsAssert.Equal(expected, actual);
    }

    [Fact]
    public void DifferentReadOnlyMemoryTypes_Success()
    {
        ReadOnlyMemory<int> expected = new[] { 1, 2, 3 };
        ReadOnlyMemory<long> actual = new[] { 1L, 2L, 3L };

        AssertionsAssert.Equal(expected, actual);
    }

    [Fact]
    public void HighlightsCollectionDifference()
    {
        IEnumerable<int> expected = Enumerable.Range(0, 20).ToArray();
        var actual = Enumerable.Range(0, 20).ToArray();
        actual[12] = 42;
        IEnumerable<int> actualEnumerable = actual;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal<int>(expected, actualEnumerable), """
            Assert.Equal() assertion failed: Item at index 12 differs.
            Expected expression: expected
            Actual expression:   actualEnumerable
            Index of first difference: 12
            Expected: [0, 1, 2, ..., 10, 11, 1̲2̲, 13, 14, ...]
            Actual:   [0, 1, 2, ..., 10, 11, 4̲2̲, 13, 14, ...]
            """);
    }

    [Fact]
    public void Collection_FailsWhenActualIsNull()
    {
        IEnumerable<int> expected = [1, 2, 3];
        IEnumerable<int>? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual), """
            Assert.Equal() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: [1, 2, 3]
            Actual:   <null>
            """);
    }

    [Fact]
    public void CollectionComparer_Success()
    {
        IEnumerable<string> expected = ["a", "b"];
        IEnumerable<string> actual = ["A", "B"];

        AssertionsAssert.Equal(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void CollectionComparer_Fails()
    {
        IEnumerable<string> expected = ["a", "b"];
        IEnumerable<string> actual = ["A", "c"];

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual, StringComparer.OrdinalIgnoreCase, "custom message"), """
            Assert.Equal() assertion failed: Item at index 1 differs.
            Message: custom message
            Expected expression: expected
            Actual expression:   actual
            Index of first difference: 1
            Expected: ["a", "̲b̲"̲]
            Actual:   ["A", "̲c̲"̲]
            """);
    }

    [Fact]
    public async Task HighlightsAsyncCollectionDifference()
    {
        var actual = Enumerable.Range(0, 20).ToArray();
        actual[12] = 42;
        var expectedEnumerable = AssertionTestHelpers.ToAsyncEnumerable(Enumerable.Range(0, 20));
        var actualEnumerable = AssertionTestHelpers.ToAsyncEnumerable(actual);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.Equal<int>(expectedEnumerable, actualEnumerable), """
            Assert.Equal() assertion failed: Item at index 12 differs.
            Expected expression: expectedEnumerable
            Actual expression:   actualEnumerable
            Index of first difference: 12
            Expected: [0, 1, 2, ..., 10, 11, 1̲2̲, 13, 14, ...]
            Actual:   [0, 1, 2, ..., 10, 11, 4̲2̲, 13, 14, ...]
            """);
    }

    [Fact]
    public async Task AsyncCollection_FailsWhenActualIsNull()
    {
        var expected = AssertionTestHelpers.ToAsyncEnumerable([1, 2, 3]);
        IAsyncEnumerable<int>? actual = null;

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.Equal(expected, actual), """
            Assert.Equal() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: [1, 2, 3]
            Actual:   <null>
            """);
    }

    [Fact]
    public async Task AsyncCollectionComparer_Success()
    {
        var expected = AssertionTestHelpers.ToAsyncEnumerable(["a", "b"]);
        var actual = AssertionTestHelpers.ToAsyncEnumerable(["A", "B"]);

        await AssertionsAssert.Equal(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AsyncCollectionComparer_Fails()
    {
        var expected = AssertionTestHelpers.ToAsyncEnumerable(["a", "b"]);
        var actual = AssertionTestHelpers.ToAsyncEnumerable(["A", "c"]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.Equal(expected, actual, StringComparer.OrdinalIgnoreCase), """
            Assert.Equal() assertion failed: Item at index 1 differs.
            Expected expression: expected
            Actual expression:   actual
            Index of first difference: 1
            Expected: ["a", "̲b̲"̲]
            Actual:   ["A", "̲c̲"̲]
            """);
    }

    [Fact]
    public void NonGenericCollection_Success()
    {
        System.Collections.IEnumerable expected = new object[] { "a", "b" };
        System.Collections.IEnumerable actual = new object[] { "A", "B" };

        AssertionsAssert.Equal(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonGenericCollection_Fails()
    {
        System.Collections.IEnumerable expected = new object[] { "a", "b", "c" };
        System.Collections.IEnumerable actual = new object[] { "A", "d", "C" };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual, StringComparer.OrdinalIgnoreCase), """
            Assert.Equal() assertion failed: Item at index 1 differs.
            Expected expression: expected
            Actual expression:   actual
            Index of first difference: 1
            Expected: ["a", "̲b̲"̲, "c"]
            Actual:   ["A", "̲d̲"̲, "C"]
            """);
    }

    [Fact]
    public void NonGenericCollection_FailsWhenActualIsNull()
    {
        System.Collections.IEnumerable expected = new object[] { 1, 2, 3 };
        System.Collections.IEnumerable? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual), """
            Assert.Equal() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: [1, 2, 3]
            Actual:   <null>
            """);
    }

    [Fact]
    public void ReadOnlySpanOfDoublesWithSignedZeroAndNaN_Success()
    {
        // Bitwise comparison would report these as different, but -0.0 equals +0.0 and NaN equals NaN.
        ReadOnlySpan<double> expected = [1.0, -0.0, double.NaN];
        ReadOnlySpan<double> actual = [1.0, 0.0, double.NaN];

        AssertionsAssert.Equal(expected, actual);
    }

    [Fact]
    public void ReadOnlySpanOfEnums_Success()
    {
        ReadOnlySpan<DayOfWeek> expected = [DayOfWeek.Monday, DayOfWeek.Friday];
        ReadOnlySpan<DayOfWeek> actual = [DayOfWeek.Monday, DayOfWeek.Friday];

        AssertionsAssert.Equal(expected, actual);
    }

    [Fact]
    public void ReadOnlySpanOfEnums_Fails()
    {
        AssertionTestHelpers.Validate(Validate, """
            Assert.Equal() assertion failed: Item at index 1 differs.
            Expected expression: expected
            Actual expression:   actual
            Index of first difference: 1
            Expected item: [Monday, F̲r̲i̲d̲a̲y̲]
            Actual item:   [Monday, S̲u̲n̲d̲a̲y̲]
            """);

        static void Validate()
        {
            ReadOnlySpan<DayOfWeek> expected = [DayOfWeek.Monday, DayOfWeek.Friday];
            ReadOnlySpan<DayOfWeek> actual = [DayOfWeek.Monday, DayOfWeek.Sunday];

            AssertionsAssert.Equal(expected, actual);
        }
    }

    [Fact]
    public void ReadOnlySpanOfBytes_Fails()
    {
        AssertionTestHelpers.Validate(Validate, """
            Assert.Equal() assertion failed: Item at index 2 differs.
            Expected expression: expected
            Actual expression:   actual
            Index of first difference: 2
            Expected item: [1, 2, 3̲, 4]
            Actual item:   [1, 2, 4̲2̲, 4]
            """);

        static void Validate()
        {
            ReadOnlySpan<byte> expected = [1, 2, 3, 4];
            ReadOnlySpan<byte> actual = [1, 2, 42, 4];

            AssertionsAssert.Equal(expected, actual);
        }
    }

    [Fact]
    public void HighlightsReadOnlySpanDifference()
    {
        AssertionTestHelpers.Validate(Validate, """
            Assert.Equal() assertion failed: Item at index 1 differs.
            Expected expression: expected
            Actual expression:   actual
            Index of first difference: 1
            Expected item: [1, 2̲, 3]
            Actual item:   [1, 4̲2̲, 3]
            """);

        static void Validate()
        {
            ReadOnlySpan<int> expected = [1, 2, 3];
            ReadOnlySpan<int> actual = [1, 42, 3];

            AssertionsAssert.Equal(expected, actual);
        }
    }

    [Fact]
    public void HighlightsReadOnlySpanLengthDifference()
    {
        AssertionTestHelpers.Validate(Validate, """
            Assert.Equal() assertion failed: Lengths differ.
            Expected expression: expected
            Actual expression:   actual
            Expected length: 3
            Actual length:   2
            Index of first difference: 2
            Expected: [1, 2, 3̲]
            Actual:   [1, 2]
            """);

        static void Validate()
        {
            ReadOnlySpan<int> expected = [1, 2, 3];
            ReadOnlySpan<int> actual = [1, 2];

            AssertionsAssert.Equal(expected, actual);
        }
    }

    [Fact]
    public void HighlightsReadOnlyMemoryLengthDifference()
    {
        ReadOnlyMemory<int> expected = new[] { 1, 2 };
        ReadOnlyMemory<int> actual = new[] { 1, 2, 3 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual), """
            Assert.Equal() assertion failed: Lengths differ.
            Expected expression: expected
            Actual expression:   actual
            Expected length: 2
            Actual length:   3
            Index of first difference: 2
            Expected: [1, 2]
            Actual:   [1, 2, 3̲]
            """);
    }

    [Fact]
    public void FormatsCircularCollections()
    {
        object? expected = null;
        var actual = new List<object?>();
        actual.Add(actual);

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual), """
            Assert.Equal() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: <null>
            Actual:   [<circular reference>]
            """);
    }

    [Fact]
    public void NotEqual_Success()
    {
        AssertionsAssert.NotEqual(1, 2);
        AssertionsAssert.NotEqual<int>([1, 2], [2, 1]);

        object expected = 42;
        object? actual = null;
        IEnumerable<int> expectedCollection = [1, 2];
        IEnumerable<int>? actualCollection = null;
        System.Collections.IEnumerable expectedNonGenericCollection = new object[] { 1, 2 };
        System.Collections.IEnumerable? actualNonGenericCollection = null;

        AssertionsAssert.NotEqual(expected, actual);
        AssertionsAssert.NotEqual(expectedCollection, actualCollection);
        AssertionsAssert.NotEqual(expectedNonGenericCollection, actualNonGenericCollection);
    }

    [Fact]
    public async Task NotEqual_AsyncEnumerableSucceedsWhenActualIsNull()
    {
        var expected = AssertionTestHelpers.ToAsyncEnumerable([1, 2]);
        IAsyncEnumerable<int>? actual = null;

        await AssertionsAssert.NotEqual(expected, actual);
    }

    [Fact]
    public void NotEqual_FailsWhenBothValuesAreNull()
    {
        object? expected = null;
        object? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEqual(expected, actual), """
            Assert.NotEqual() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected: <null>
            Actual:       <null>
            """);
    }

    [Fact]
    public void NotEqual_Fails()
    {
        var expected = 42;
        var actual = 42;

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEqual(expected, actual), """
            Assert.NotEqual() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected: 42
            Actual:       42
            """);
    }

    [Fact]
    public async Task NotEqual_AsyncEnumerableFails()
    {
        var expected = AssertionTestHelpers.ToAsyncEnumerable([1, 2]);
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 2]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.NotEqual(expected, actual), """
            Assert.NotEqual() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected: [1, 2]
            Actual:       [1, 2]
            """);
    }

    [Fact]
    public void NotEqualWithTolerance_Fails()
    {
        var expected = 1.0;
        var actual = 1.1;
        var tolerance = 0.2;

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEqual(expected, actual, tolerance, "custom message"), """
            Assert.NotEqual() assertion failed.
            Message: custom message
            Expected expression: expected
            Actual expression:   actual
            Not expected: 1
            Actual:       1.1
            Tolerance: 0.2
            """);
    }

    [Fact]
    public void NotEqualWithNFloatTolerance_Success()
    {
        var expected = (NFloat)1;
        var actual = (NFloat)1.3;
        var tolerance = (NFloat)0.2;

        AssertionsAssert.NotEqual(expected, actual, tolerance);
    }

    [Fact]
    public void NotEqualWithNFloatTolerance_Fails()
    {
        var expected = (NFloat)1;
        var actual = (NFloat)1.1;
        var tolerance = (NFloat)0.2;

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEqual(expected, actual, tolerance), """
            Assert.NotEqual() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected: 1
            Actual:       1.1
            Tolerance: 0.2
            """);
    }

    [Fact]
    public void NotEqual_ReadOnlySpanOfBoxedNumbers_IsTheComplementOfEqual()
    {
        object[] expected = [1, 2.5];
        object[] actual = [1L, 2.5f];

        AssertionsAssert.Equal<object>(expected.AsSpan(), actual.AsSpan());
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual<object>(expected.AsSpan(), actual.AsSpan()));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual<object>(expected.AsMemory(), actual.AsMemory()));
    }

    [Fact]
    public void NotEqual_ReadOnlySpanOfArrays_IsTheComplementOfEqual()
    {
        int[][] expected = [[1, 2]];
        int[][] actual = [[1, 2]];

        AssertionsAssert.Equal<int[]>(expected.AsSpan(), actual.AsSpan());
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual<int[]>(expected.AsSpan(), actual.AsSpan()));
    }

    [Fact]
    public void NotEqual_ReadOnlySpanOfPrimitives()
    {
        int[] values = [1, 2, 3];

        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual<int>([1, 2, 3], values.AsSpan()));
        AssertionsAssert.NotEqual<int>([1, 2, 4], values.AsSpan());
        AssertionsAssert.NotEqual<int>([1, 2], values.AsSpan());
    }

    [Fact]
    public void NotEqual_EnumeratesASingleUseSequenceOnlyOnce()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEqual(AssertionTestHelpers.SingleUse(1, 2), AssertionTestHelpers.SingleUse(1, 2)), """
            Assert.NotEqual() assertion failed.
            Expected expression: AssertionTestHelpers.SingleUse(1, 2)
            Actual expression:   AssertionTestHelpers.SingleUse(1, 2)
            Not expected: [1, 2]
            Actual:       [1, 2]
            """);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(AssertionTestHelpers.SingleUse(1, 2), AssertionTestHelpers.SingleUse(1, 2), EqualityComparer<int>.Default));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(AssertionTestHelpers.SingleUse(1, 2), AssertionTestHelpers.SingleUse(1L, 2L)));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual((System.Collections.IEnumerable)AssertionTestHelpers.SingleUse(1, 2), (System.Collections.IEnumerable)AssertionTestHelpers.SingleUse(1, 2)));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual((System.Collections.IEnumerable)AssertionTestHelpers.SingleUse(1, 2), (System.Collections.IEnumerable)AssertionTestHelpers.SingleUse(1, 2), comparer: (System.Collections.IEqualityComparer?)null));
    }

    [Fact]
    public void BoxedValuesOfTheSameType_KeepEqualsSemantics()
    {
        AssertionsAssert.Equal<object>(double.NaN, double.NaN);
        AssertionsAssert.Equal<object>(0.0, -0.0);
        AssertionsAssert.Equal<object>(1.0m, 1.00m);
        AssertionsAssert.NotEqual<object>(1, 2);
        AssertionsAssert.NotEqual<object>(decimal.MaxValue, decimal.MinValue);
        AssertionsAssert.Equal<object>((nint)42, 42L);
        AssertionsAssert.Equal<object>(42L, (nuint)42);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal<object>(1, 2));
    }

    [Fact]
    public void Enums_SameEnumType_Success()
    {
        AssertionsAssert.Equal(Status.Active, Status.Active);
        AssertionsAssert.Equal<object>(Status.Closed, Status.Closed);
    }

    [Fact]
    public void Enums_DifferentEnumTypesWithSameNumericValue_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(Status.Active, Kind.First), """
            Assert.Equal() assertion failed.
            Expected expression: Status.Active
            Actual expression:   Kind.First
            Expected: Active
            Actual:   First
            """);
    }

    [Fact]
    public void Enums_EnumAndIntegerWithSameNumericValue_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(1, Status.Active), """
            Assert.Equal() assertion failed.
            Expected expression: 1
            Actual expression:   Status.Active
            Expected: 1
            Actual:   Active
            """);
    }

    [Fact]
    public void Enums_NotEqualAcceptsDifferentEnumTypesWithSameNumericValue()
    {
        AssertionsAssert.NotEqual(Status.Active, Kind.First);
        AssertionsAssert.NotEqual(1, Status.Active);
    }

    private enum Status
    {
        Active = 1,
        Closed = 2,
    }

    private enum Kind
    {
        First = 1,
        Second = 2,
    }

    private readonly struct ImplicitlyConvertibleValue : IEquatable<ImplicitlyConvertibleValue>
    {
        private readonly int _value;

        public ImplicitlyConvertibleValue(int value)
        {
            _value = value;
        }

        public static implicit operator ImplicitlyConvertibleValue(int value) => new(value);

        public bool Equals(ImplicitlyConvertibleValue other) => _value == other._value;

        public override bool Equals([NotNullWhen(true)] object? obj) => obj is ImplicitlyConvertibleValue other && Equals(other);

        public override int GetHashCode() => _value;
    }

    [Fact]
    public void BoxedReadOnlyMemory_EqualAndNotEqualDisagree()
    {
        object expected = new ReadOnlyMemory<int>([1, 2, 3]);
        object actual = new ReadOnlyMemory<int>([1, 2, 3]);

        AssertionsAssert.Equal(expected, actual);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(expected, actual));
    }

    [Fact]
    public void BoxedReadOnlyMemory_NotEqualSucceedsForDifferentContent()
    {
        object expected = new ReadOnlyMemory<int>([1, 2, 3]);
        object actual = new ReadOnlyMemory<int>([1, 2, 4]);

        AssertionsAssert.NotEqual(expected, actual);
    }

    [Fact]
    public void DoublePrecision_IsNotATolerance()
    {
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal(3.14, 3.0, 2));
        AssertionsAssert.NotEqual(3.14, 3.0, 2);
    }

    [Fact]
    public void DecimalPrecision_IsNotATolerance()
    {
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal(3.14m, 3.0m, 2));
        AssertionsAssert.NotEqual(3.14m, 3.0m, 2);
    }

    [Fact]
    public void Precision_RoundsBeforeComparing()
    {
        AssertionsAssert.Equal(3.14159, 3.1416, 3);
        AssertionsAssert.Equal(0.125, 0.12, 2);
        AssertionsAssert.Equal(double.NaN, double.NaN, 2);
        AssertionsAssert.Equal(3.14159f, 3.1416f, 3);
        AssertionsAssert.Equal(3.14159m, 3.1416m, 3);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(3.14159, 3.1416, 3));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(3.14159f, 3.1416f, 3));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(3.14159m, 3.1416m, 3));
    }

    [Fact]
    public void Precision_MidpointRounding()
    {
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal(0.125, 0.13, 2));
        AssertionsAssert.Equal(0.125, 0.13, 2, MidpointRounding.AwayFromZero);
        AssertionsAssert.NotEqual(0.125, 0.13, 2, MidpointRounding.ToEven);
        AssertionsAssert.Equal(0.125f, 0.13f, 2, MidpointRounding.AwayFromZero);
        AssertionsAssert.NotEqual(0.125f, 0.13f, 2, MidpointRounding.ToEven);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal(2.5m, 3m, 0));
        AssertionsAssert.Equal(2.5m, 3m, 0, MidpointRounding.AwayFromZero);
        AssertionsAssert.NotEqual(2.5m, 3m, 0, MidpointRounding.ToEven);
    }

    [Fact]
    public void DoublePrecision_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(3.14159, 3.1, 2, "custom message"), """
            Assert.Equal() assertion failed.
            Message: custom message
            Expected expression: 3.14159
            Actual expression:   3.1
            Expected: 3.14 (rounded from 3.14159)
            Actual:   3.1 (rounded from 3.1)
            Precision: 2 decimal places
            """);
    }

    [Fact]
    public void SinglePrecision_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(0.125f, 0.25f, 1), """
            Assert.Equal() assertion failed.
            Expected expression: 0.125f
            Actual expression:   0.25f
            Expected: 0.1 (rounded from 0.125)
            Actual:   0.2 (rounded from 0.25)
            Precision: 1 decimal place
            """);
    }

    [Fact]
    public void DecimalPrecision_NotEqualFailsWithRounding()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEqual(2.5m, 3m, 0, MidpointRounding.AwayFromZero), """
            Assert.NotEqual() assertion failed.
            Expected expression: 2.5m
            Actual expression:   3m
            Not expected: 3 (rounded from 2.5)
            Actual:       3 (rounded from 3)
            Precision: 0 decimal places
            Rounding:  AwayFromZero
            """);
    }

    [Fact]
    public void DateTimePrecision()
    {
        var expected = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        AssertionsAssert.Equal(expected, expected.AddMilliseconds(500), TimeSpan.FromSeconds(1));
        AssertionsAssert.Equal(expected, expected.AddSeconds(-1), TimeSpan.FromSeconds(1));
        AssertionsAssert.NotEqual(expected, expected.AddSeconds(2), TimeSpan.FromSeconds(1));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(expected, expected.AddMilliseconds(-500), TimeSpan.FromSeconds(1)));
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, expected.AddSeconds(2), TimeSpan.FromSeconds(1)), """
            Assert.Equal() assertion failed.
            Expected expression: expected
            Actual expression:   expected.AddSeconds(2)
            Expected: 2024-01-02T03:04:05.0000000Z
            Actual:   2024-01-02T03:04:07.0000000Z
            Difference: 00:00:02
            Precision:  00:00:01
            """);
    }

    [Fact]
    public void DateTimeOffsetPrecision()
    {
        var expected = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);

        AssertionsAssert.Equal(expected, expected.ToOffset(TimeSpan.FromHours(2)), TimeSpan.Zero);
        AssertionsAssert.Equal(expected, expected.AddMilliseconds(500), TimeSpan.FromSeconds(1));
        AssertionsAssert.NotEqual(expected, expected.AddSeconds(2), TimeSpan.FromSeconds(1));
        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEqual(expected, expected.AddMilliseconds(500), TimeSpan.FromSeconds(1)), """
            Assert.NotEqual() assertion failed.
            Expected expression: expected
            Actual expression:   expected.AddMilliseconds(500)
            Not expected: 2024-01-02T03:04:05.0000000+00:00
            Actual:       2024-01-02T03:04:05.5000000+00:00
            Difference: 00:00:00.5000000
            Precision:  00:00:01
            """);
    }

    [Fact]
    public void ScalarComparer_String_UsesComparer()
    {
        AssertionsAssert.Equal("a", "A", StringComparer.OrdinalIgnoreCase);
        AssertionsAssert.NotEqual("a", "b", StringComparer.OrdinalIgnoreCase);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual("a", "A", StringComparer.OrdinalIgnoreCase));
        AssertionsAssert.Equal("a", "a", (IEqualityComparer<string>?)null);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal("a", "A", (IEqualityComparer<string>?)null));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual("a", "a", (IEqualityComparer<string>?)null));
    }

    [Fact]
    public void ScalarComparer_String_Fails()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal("abc", "ABD", StringComparer.OrdinalIgnoreCase), """
            Assert.Equal() assertion failed.
            Expected expression: "abc"
            Actual expression:   "ABD"
            Index of first difference: 2
            Expected: "abc̲"
            Actual:   "ABD̲"
            """);
    }

    [Fact]
    public void ScalarComparer_CustomComparer()
    {
        var comparer = EqualityComparer<int>.Create((x, y) => Math.Abs(x) == Math.Abs(y), Math.Abs);

        AssertionsAssert.Equal(1, -1, comparer);
        AssertionsAssert.NotEqual(1, 2, comparer);
        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEqual(1, -1, comparer, "custom message"), """
            Assert.NotEqual() assertion failed.
            Message: custom message
            Expected expression: 1
            Actual expression:   -1
            Not expected: 1
            Actual:       -1
            """);
    }

    [Fact]
    public void ScalarComparer_AppliesToWholeCollection()
    {
        var expected = new HashSet<int> { 1, 2 };
        var actual = new HashSet<int> { 2, 1 };

        AssertionsAssert.Equal(expected, actual, HashSet<int>.CreateSetComparer());
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(expected, actual, HashSet<int>.CreateSetComparer()));
    }

    [Fact]
    public void NotEqual_StringOptions()
    {
        AssertionsAssert.NotEqual("Hello", "hello");
        AssertionsAssert.NotEqual("line1\r\nline2", "line1\nline2");
        AssertionsAssert.NotEqual("line1\r\nline2", "LINE1\nline2", ignoreLineEndingDifferences: true);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual("line1\r\nline2\rline3", "LINE1\nline2\nline3", ignoreCase: true, ignoreLineEndingDifferences: true));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual((string?)null, null));
        AssertionsAssert.NotEqual(null, "a", ignoreCase: true, ignoreLineEndingDifferences: true);
        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEqual("Hello", "hello", ignoreCase: true), """
            Assert.NotEqual() assertion failed.
            Expected expression: "Hello"
            Actual expression:   "hello"
            Not expected: "Hello"
            Actual:       "hello"
            """);
    }

    [Fact]
    public void NotEqual_CharSpanOptions()
    {
        AssertionsAssert.NotEqual("Hello".AsSpan(), "hello".AsSpan());
        AssertionsAssert.NotEqual("a\r\nb".AsSpan(), "a\nc".AsSpan(), ignoreLineEndingDifferences: true);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual("a\r\nB".AsSpan(), "A\nb".AsSpan(), ignoreCase: true, ignoreLineEndingDifferences: true));
        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEqual("Hello".AsSpan(), "hello".AsSpan(), ignoreCase: true), """
            Assert.NotEqual() assertion failed.
            Expected expression: "Hello".AsSpan()
            Actual expression:   "hello".AsSpan()
            Not expected: "Hello"
            Actual:       "hello"
            """);
    }

    [Theory]
    [InlineData("a\r\nb", "a\nb", true)]
    [InlineData("a\rb", "a\nb", true)]
    [InlineData("a\r\n", "a\r", true)]
    [InlineData("\r\n\r\n", "\n\n", true)]
    [InlineData("a\r\r\nb", "a\n\nb", true)]
    [InlineData("a\r\nb", "a\n\nb", false)]
    [InlineData("a\r\nb", "ab", false)]
    [InlineData("a\nb", "a\nbc", false)]
    [InlineData("", "\r", false)]
    [InlineData("A\r\nb", "a\nB", true)]
    public void IgnoreLineEndingDifferences_MatchesNormalizedComparison(string expected, string actual, bool equalIgnoringCase)
    {
        var normalizedExpected = expected.ReplaceLineEndings("\n");
        var normalizedActual = actual.ReplaceLineEndings("\n");
        var equalOrdinal = string.Equals(normalizedExpected, normalizedActual, StringComparison.Ordinal);
        AssertionsAssert.Equal(equalIgnoringCase, string.Equals(normalizedExpected, normalizedActual, StringComparison.OrdinalIgnoreCase));

        AssertOutcome(equalOrdinal, () => AssertionsAssert.Equal(expected, actual, ignoreLineEndingDifferences: true), () => AssertionsAssert.NotEqual(expected, actual, ignoreLineEndingDifferences: true));
        AssertOutcome(equalIgnoringCase, () => AssertionsAssert.Equal(expected, actual, ignoreCase: true, ignoreLineEndingDifferences: true), () => AssertionsAssert.NotEqual(expected, actual, ignoreCase: true, ignoreLineEndingDifferences: true));
        AssertOutcome(equalOrdinal, () => AssertionsAssert.Equal(expected.AsSpan(), actual.AsSpan(), ignoreLineEndingDifferences: true), () => AssertionsAssert.NotEqual(expected.AsSpan(), actual.AsSpan(), ignoreLineEndingDifferences: true));
    }

    [Fact]
    public void IgnoreLineEndingDifferences_DoesNotAllocateWhenPassing()
    {
        var expected = string.Concat(Enumerable.Repeat("line\r\n", 1000));
        var actual = string.Concat(Enumerable.Repeat("LINE\n", 1000));
        AssertionsAssert.Equal(expected, actual, ignoreCase: true, ignoreLineEndingDifferences: true);
        AssertionsAssert.Equal(expected.AsSpan(), actual.AsSpan(), ignoreCase: true, ignoreLineEndingDifferences: true);

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread();
        AssertionsAssert.Equal(expected, actual, ignoreCase: true, ignoreLineEndingDifferences: true);
        AssertionsAssert.Equal(expected.AsSpan(), actual.AsSpan(), ignoreCase: true, ignoreLineEndingDifferences: true);
        allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBytes;

        AssertionsAssert.True(allocatedBytes < expected.Length, $"Allocated {allocatedBytes} bytes");
    }

    [Fact]
    public void NullLists_AreEqual()
    {
        List<int>? expected = null;
        List<int>? actual = null;

        AssertionsAssert.Equal(expected, actual);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(expected, actual));
    }

    [Fact]
    public void NullEnumerables_AreEqual()
    {
        IEnumerable<int>? expected = null;
        IEnumerable<int>? actual = null;
        IEnumerable<long>? actualOfOtherType = null;
        System.Collections.IEnumerable? expectedNonGeneric = null;
        System.Collections.IEnumerable? actualNonGeneric = null;

        AssertionsAssert.Equal(expected, actual);
        AssertionsAssert.Equal(expected, actual, EqualityComparer<int>.Default);
        AssertionsAssert.Equal(expected, actualOfOtherType);
        AssertionsAssert.Equal(expectedNonGeneric, actualNonGeneric);
        AssertionsAssert.Equal(expectedNonGeneric, actualNonGeneric, StringComparer.Ordinal);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(expected, actual));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(expected, actual, EqualityComparer<int>.Default));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(expected, actualOfOtherType));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(expectedNonGeneric, actualNonGeneric));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(expectedNonGeneric, actualNonGeneric, StringComparer.Ordinal));
    }

    [Fact]
    public void NullExpectedList_Fails()
    {
        List<int>? expected = null;
        var actual = new List<int> { 1, 2 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual), """
            Assert.Equal() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: <null>
            Actual:   [1, 2]
            """);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal(expected, actual, EqualityComparer<int>.Default));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal(expected, new List<long> { 1 }));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal((System.Collections.IEnumerable?)expected, actual));
        AssertionsAssert.NotEqual(expected, actual);
        AssertionsAssert.NotEqual(expected, actual, EqualityComparer<int>.Default);
        AssertionsAssert.NotEqual(expected, new List<long> { 1 });
        AssertionsAssert.NotEqual((System.Collections.IEnumerable?)expected, actual);
    }

    [Fact]
    public void NullLiteralExpected_Compiles()
    {
        var actual = new List<int> { 1 };
        List<int>? nullActual = null;

        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal(null, actual));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal(actual, nullActual));
        AssertionsAssert.Equal(null, nullActual);
        AssertionsAssert.NotEqual(null, actual);
    }

    [Fact]
    public async Task NullAsyncEnumerables()
    {
        IAsyncEnumerable<int>? expected = null;
        IAsyncEnumerable<int>? actual = null;
        IAsyncEnumerable<long>? actualOfOtherType = null;

        await AssertionsAssert.Equal(expected, actual);
        await AssertionsAssert.Equal(expected, actual, EqualityComparer<int>.Default);
        await AssertionsAssert.Equal(expected, actualOfOtherType);
        await AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(expected, actual));
        await AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(expected, actual, EqualityComparer<int>.Default));
        await AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(expected, actualOfOtherType));
        await AssertionsAssert.NotEqual(expected, AssertionTestHelpers.ToAsyncEnumerable([1]));
        await AssertionsAssert.NotEqual(expected, AssertionTestHelpers.ToAsyncEnumerable([1L]));
        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.Equal(expected, AssertionTestHelpers.ToAsyncEnumerable([1, 2])), """
            Assert.Equal() assertion failed.
            Expected expression: expected
            Actual expression:   AssertionTestHelpers.ToAsyncEnumerable([1, 2])
            Expected: <null>
            Actual:   [1, 2]
            """);
        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.Equal(expected, AssertionTestHelpers.ToAsyncEnumerable([1L, 2L])), """
            Assert.Equal() assertion failed.
            Expected expression: expected
            Actual expression:   AssertionTestHelpers.ToAsyncEnumerable([1L, 2L])
            Expected: <null>
            Actual:   [1, 2]
            """);
    }

    [Fact]
    public async Task MixedSyncAndAsyncEnumerables_Success()
    {
        await AssertionsAssert.Equal(new List<int> { 1, 2 }, AssertionTestHelpers.ToAsyncEnumerable([1, 2]));
        await AssertionsAssert.Equal(AssertionTestHelpers.ToAsyncEnumerable([1, 2]), new List<int> { 1, 2 });
        await AssertionsAssert.Equal((IEnumerable<int>?)null, (IAsyncEnumerable<int>?)null);
        await AssertionsAssert.Equal((IAsyncEnumerable<int>?)null, (IEnumerable<int>?)null);
    }

    [Fact]
    public async Task MixedSyncAndAsyncEnumerables_Fails()
    {
        IEnumerable<int> expected = [1, 2, 3];
        var actual = AssertionTestHelpers.ToAsyncEnumerable([1, 42, 3]);

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.Equal(expected, actual), """
            Assert.Equal() assertion failed: Item at index 1 differs.
            Expected expression: expected
            Actual expression:   actual
            Index of first difference: 1
            Expected: [1, 2̲, 3]
            Actual:   [1, 4̲2̲, 3]
            """);
        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.Equal(actual, expected), """
            Assert.Equal() assertion failed: Item at index 1 differs.
            Expected expression: actual
            Actual expression:   expected
            Index of first difference: 1
            Expected: [1, 4̲2̲, 3]
            Actual:   [1, 2̲, 3]
            """);
    }

    [Fact]
    public async Task MixedSyncAndAsyncEnumerables_FailsWhenOneIsNull()
    {
        IEnumerable<int> expected = [1, 2];
        IAsyncEnumerable<int>? actual = null;
        IEnumerable<int>? nullExpected = null;
        var asyncExpected = AssertionTestHelpers.ToAsyncEnumerable([1, 2]);
        IEnumerable<int>? nullActual = null;

        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.Equal(expected, actual), """
            Assert.Equal() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Expected: [1, 2]
            Actual:   <null>
            """);
        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.Equal(nullExpected, asyncExpected), """
            Assert.Equal() assertion failed.
            Expected expression: nullExpected
            Actual expression:   asyncExpected
            Expected: <null>
            Actual:   [1, 2]
            """);
        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.Equal(AssertionTestHelpers.ToAsyncEnumerable([1, 2]), nullActual), """
            Assert.Equal() assertion failed.
            Expected expression: AssertionTestHelpers.ToAsyncEnumerable([1, 2])
            Actual expression:   nullActual
            Expected: [1, 2]
            Actual:   <null>
            """);
    }

    [Fact]
    public async Task AsyncEnumerablesOfDifferentTypes()
    {
        await AssertionsAssert.Equal(AssertionTestHelpers.ToAsyncEnumerable([1, 2]), AssertionTestHelpers.ToAsyncEnumerable([1L, 2L]));
        await AssertionsAssert.NotEqual(AssertionTestHelpers.ToAsyncEnumerable([1, 2]), AssertionTestHelpers.ToAsyncEnumerable([1L, 3L]));
        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.Equal(AssertionTestHelpers.ToAsyncEnumerable([1, 2]), AssertionTestHelpers.ToAsyncEnumerable([1L, 3L])), """
            Assert.Equal() assertion failed: Item at index 1 differs.
            Expected expression: AssertionTestHelpers.ToAsyncEnumerable([1, 2])
            Actual expression:   AssertionTestHelpers.ToAsyncEnumerable([1L, 3L])
            Index of first difference: 1
            Expected: [1, 2̲]
            Actual:   [1, 3̲]
            """);
        IAsyncEnumerable<long>? nullActual = null;
        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.Equal(AssertionTestHelpers.ToAsyncEnumerable([1, 2]), nullActual), """
            Assert.Equal() assertion failed.
            Expected expression: AssertionTestHelpers.ToAsyncEnumerable([1, 2])
            Actual expression:   nullActual
            Expected: [1, 2]
            Actual:   <null>
            """);
        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.NotEqual(AssertionTestHelpers.ToAsyncEnumerable([1, 2]), AssertionTestHelpers.ToAsyncEnumerable([1L, 2L])), """
            Assert.NotEqual() assertion failed.
            Expected expression: AssertionTestHelpers.ToAsyncEnumerable([1, 2])
            Actual expression:   AssertionTestHelpers.ToAsyncEnumerable([1L, 2L])
            Not expected: [1, 2]
            Actual:       [1, 2]
            """);
    }

    [Fact]
    public async Task NotEqual_AsyncEnumerablesWithComparer()
    {
        await AssertionsAssert.NotEqual(AssertionTestHelpers.ToAsyncEnumerable(["a", "b"]), AssertionTestHelpers.ToAsyncEnumerable(["A", "c"]), StringComparer.OrdinalIgnoreCase);
        IAsyncEnumerable<string>? nullActual = null;
        await AssertionsAssert.NotEqual(AssertionTestHelpers.ToAsyncEnumerable(["a", "b"]), nullActual, StringComparer.OrdinalIgnoreCase);
        await AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(null, nullActual, StringComparer.OrdinalIgnoreCase));
        await AssertionTestHelpers.ValidateAsync(() => AssertionsAssert.NotEqual(AssertionTestHelpers.ToAsyncEnumerable(["a", "b"]), AssertionTestHelpers.ToAsyncEnumerable(["A", "B"]), StringComparer.OrdinalIgnoreCase), """
            Assert.NotEqual() assertion failed.
            Expected expression: AssertionTestHelpers.ToAsyncEnumerable(["a", "b"])
            Actual expression:   AssertionTestHelpers.ToAsyncEnumerable(["A", "B"])
            Not expected: ["a", "b"]
            Actual:       ["A", "B"]
            """);
    }

#pragma warning disable CA1814 // Multidimensional arrays are the subject of these tests
    [Fact]
    public void MultiDimensionalArrays_CompareShapeAndItems()
    {
        AssertionsAssert.Equal(new int[,] { { 1, 2 }, { 3, 4 } }, new int[,] { { 1, 2 }, { 3, 4 } });
        AssertionsAssert.NotEqual(new int[1, 2], new int[2, 1]);
        AssertionsAssert.NotEqual(new int[,] { { 1, 2 } }, new int[] { 1, 2 });
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal(new int[1, 2], new int[2, 1]));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(new int[,] { { 1, 2 } }, new int[,] { { 1, 2 } }));
        AssertionsAssert.Equal(new object[] { new int[,] { { 1 } } }, new object[] { new int[,] { { 1 } } });
        AssertionsAssert.NotEqual(new object[] { new int[1, 2] }, new object[] { new int[2, 1] });
        AssertionsAssert.Equal(new int[,] { { 1, 2 } }, new int[,] { { 1, 2 } }, comparer: EqualityComparer<object>.Default);
    }

    [Fact]
    public void MultiDimensionalArrays_FailWhenDimensionsDiffer()
    {
        var expected = new int[1, 2];
        var actual = new int[2, 1];

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual), """
            Assert.Equal() assertion failed: Dimensions differ.
            Expected expression: expected
            Actual expression:   actual
            Expected dimensions: [1, 2]
            Actual dimensions:   [2, 1]
            Expected: [0, 0]
            Actual:   [0, 0]
            """);
    }

    [Fact]
    public void MultiDimensionalArrays_FailWhenItemsDiffer()
    {
        var expected = new int[,] { { 1, 2 }, { 3, 4 } };
        var actual = new int[,] { { 1, 2 }, { 3, 5 } };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual), """
            Assert.Equal() assertion failed: Item at index 3 differs.
            Expected expression: expected
            Actual expression:   actual
            Index of first difference: 3
            Expected: [1, 2, 3, 4̲]
            Actual:   [1, 2, 3, 5̲]
            """);
    }
#pragma warning restore CA1814

    [Fact]
    public void NestedByteArrays_DoNotBoxItems()
    {
        var expected = Enumerable.Range(0, 4).Select(_ => new byte[64 * 1024]).ToList();
        var actual = Enumerable.Range(0, 4).Select(_ => new byte[64 * 1024]).ToList();
        byte[][] expectedJagged = [.. expected];
        byte[][] actualJagged = [.. actual];
        AssertionsAssert.Equal(expected, actual);
        AssertionsAssert.Equal(expectedJagged, actualJagged);

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread();
        AssertionsAssert.Equal(expected, actual);
        AssertionsAssert.Equal(expectedJagged, actualJagged);
        allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBytes;

        AssertionsAssert.True(allocatedBytes < 16 * 1024, $"Allocated {allocatedBytes} bytes");
        actual[3][^1] = 1;
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal(expected, actual));
        AssertionsAssert.NotEqual(expected, actual);
    }

    [Fact]
    public void NestedListsWithDifferentRuntimeTypes_CompareItemsAsValues()
    {
        var expected = new List<object> { new List<object> { 1, "a" }, new object[] { 2L } };
        var actual = new List<object> { new object[] { 1L, "a" }, new List<object> { 2 } };

        AssertionsAssert.Equal(expected, actual);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(expected, actual));
        AssertionsAssert.Equal(new List<IReadOnlyList<double>> { new[] { 0.0, double.NaN } }, new List<IReadOnlyList<double>> { ImmutableArray.Create(-0.0, double.NaN) });
        AssertionsAssert.NotEqual(new List<int[]> { new[] { 1, 2 } }, new List<int[]> { new[] { 1 } });
    }

    [Fact]
    public void ReadOnlyLists_OnlyTrustPositiveDefaultComparerResults()
    {
        IReadOnlyList<object> expected = ImmutableArray.Create<object>(1, new[] { 2 });
        IReadOnlyList<object> actual = new List<object> { 1L, new[] { 2 } }.AsReadOnly();

        AssertionsAssert.Equal(expected, actual);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(expected, actual));
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal<int>(ImmutableArray.Create(1, 2), new List<int> { 1, 3 }.AsReadOnly()), """
            Assert.Equal() assertion failed: Item at index 1 differs.
            Expected expression: ImmutableArray.Create(1, 2)
            Actual expression:   new List<int> { 1, 3 }.AsReadOnly()
            Index of first difference: 1
            Expected: [1, 2̲]
            Actual:   [1, 3̲]
            """);
    }

    [Fact]
    public void SelfContainingLists_DoNotOverflowTheStack()
    {
        var expected = new List<object?>();
        expected.Add(expected);
        var actual = new List<object?>();
        actual.Add(actual);

        AssertionsAssert.Equal(expected, actual);
        AssertionsAssert.Equal((object)expected, (object)actual);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(expected, actual));
    }

    [Fact]
    public void SelfContainingLists_ReportOtherDifferences()
    {
        var expected = new List<object?>();
        expected.Add(expected);
        expected.Add(1);
        var actual = new List<object?>();
        actual.Add(actual);
        actual.Add(2);

        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal(expected, actual));
        AssertionsAssert.NotEqual(expected, actual);
    }

    [Fact]
    public void MutuallyContainingLists_DoNotOverflowTheStack()
    {
        var expectedA = new List<object?>();
        var expectedB = new List<object?> { expectedA };
        expectedA.Add(expectedB);
        var actualA = new List<object?>();
        var actualB = new List<object?> { actualA };
        actualA.Add(actualB);

        AssertionsAssert.Equal(expectedA, actualA);
        AssertionsAssert.Equal(expectedA, actualB);
    }

    [Fact]
    public void DictionariesWithCollectionValues_CompareValuesByContent()
    {
        var expected = new Dictionary<string, List<int>>(StringComparer.Ordinal) { ["a"] = [1, 2] };
        var actual = new Dictionary<string, List<int>>(StringComparer.Ordinal) { ["a"] = [1, 2] };

        AssertionsAssert.Equal(expected, actual);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(expected, actual));

        actual["a"].Add(3);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal(expected, actual));
        AssertionsAssert.NotEqual(expected, actual);
    }

    [Fact]
    public void KeyValuePairsAndTuplesWithCollections_CompareComponentsByContent()
    {
        AssertionsAssert.Equal(new KeyValuePair<string, int[]>("a", [1]), new KeyValuePair<string, int[]>("a", [1]));
        AssertionsAssert.Equal(new KeyValuePair<string, object>("a", 1), new KeyValuePair<string, object>("a", 1L));
        AssertionsAssert.NotEqual(new KeyValuePair<string, int[]>("a", [1]), new KeyValuePair<string, int[]>("a", [2]));
        AssertionsAssert.NotEqual(new KeyValuePair<string, int[]>("a", [1]), new KeyValuePair<string, int[]>("b", [1]));
        AssertionsAssert.Equal(("a", new[] { 1 }), ("a", new[] { 1 }));
        AssertionsAssert.Equal(new List<(string, List<int>)> { ("a", [1, 2]) }, new List<(string, List<int>)> { ("a", [1, 2]) });
        AssertionsAssert.NotEqual(("a", new[] { 1 }), ("a", new[] { 2 }));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(("a", new[] { 1 }), ("a", new[] { 1 })));
        AssertionsAssert.Equal<object>((1, 2, 3, 4, 5, 6, 7, new[] { 8 }), (1, 2, 3, 4, 5, 6, 7, new[] { 8 }));
        AssertionsAssert.NotEqual<object>((1, 2, 3, 4, 5, 6, 7, new[] { 8 }), (1, 2, 3, 4, 5, 6, 7, new[] { 9 }));
    }

    [Fact]
    public void DefaultImmutableArrays_AreEqual()
    {
        AssertionsAssert.Equal(default(ImmutableArray<int>), default(ImmutableArray<int>));
        AssertionsAssert.Equal<ImmutableArray<int>>(default, default);
        AssertionsAssert.Equal((System.Collections.IEnumerable)default(ImmutableArray<int>), default(ImmutableArray<int>));
        AssertionsAssert.Equal(new[] { default(ImmutableArray<int>) }, new[] { default(ImmutableArray<int>) });
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(default(ImmutableArray<int>), default(ImmutableArray<int>)));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual((System.Collections.IEnumerable)default(ImmutableArray<int>), default(ImmutableArray<int>)));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEqual(new[] { default(ImmutableArray<int>) }, new[] { default(ImmutableArray<int>) }));
        AssertionsAssert.NotEqual(ImmutableArray<int>.Empty, default(ImmutableArray<int>));
        AssertionsAssert.NotEqual(new[] { ImmutableArray<int>.Empty }, new[] { default(ImmutableArray<int>) });
    }

    [Fact]
    public void DefaultImmutableArray_IsNotEqualToEmpty()
    {
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(default(ImmutableArray<int>), ImmutableArray<int>.Empty), """
            Assert.Equal() assertion failed.
            Expected expression: default(ImmutableArray<int>)
            Actual expression:   ImmutableArray<int>.Empty
            Expected: <null>
            Actual:   []
            """);
    }

    public static TheoryData<string, double, double, double> ToleranceComplementCases()
    {
        var data = new TheoryData<string, double, double, double>();
        foreach (var type in new[] { "Half", "float", "double", "NFloat" })
        {
            data.Add(type, 1, 1, 0);
            data.Add(type, 1, 1.25, 0.5);
            data.Add(type, 1, 2, 0.5);
            data.Add(type, double.NaN, double.NaN, 0);
            data.Add(type, double.NaN, 1, double.PositiveInfinity);
            data.Add(type, double.PositiveInfinity, double.PositiveInfinity, 0);
            data.Add(type, double.PositiveInfinity, double.NegativeInfinity, 1);
            data.Add(type, double.NegativeInfinity, double.NegativeInfinity, 1);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ToleranceComplementCases))]
    public void Tolerance_EqualAndNotEqualAreComplements(string type, double expected, double actual, double tolerance)
    {
        var areEqual = expected.Equals(actual) || Math.Abs(expected - actual) <= tolerance;
        switch (type)
        {
            case "Half":
                AssertOutcome(areEqual, () => AssertionsAssert.Equal((Half)expected, (Half)actual, (Half)tolerance), () => AssertionsAssert.NotEqual((Half)expected, (Half)actual, (Half)tolerance));
                break;

            case "float":
                AssertOutcome(areEqual, () => AssertionsAssert.Equal((float)expected, (float)actual, (float)tolerance), () => AssertionsAssert.NotEqual((float)expected, (float)actual, (float)tolerance));
                break;

            case "double":
                AssertOutcome(areEqual, () => AssertionsAssert.Equal(expected, actual, tolerance), () => AssertionsAssert.NotEqual(expected, actual, tolerance));
                break;

            default:
                AssertOutcome(areEqual, () => AssertionsAssert.Equal((NFloat)expected, (NFloat)actual, (NFloat)tolerance), () => AssertionsAssert.NotEqual((NFloat)expected, (NFloat)actual, (NFloat)tolerance));
                break;
        }
    }

    private static void AssertOutcome(bool areEqual, Action equal, Action notEqual)
    {
        if (areEqual)
        {
            equal();
            AssertionsAssert.Throws<AssertionException>(notEqual);
        }
        else
        {
            AssertionsAssert.Throws<AssertionException>(equal);
            notEqual();
        }
    }
}
