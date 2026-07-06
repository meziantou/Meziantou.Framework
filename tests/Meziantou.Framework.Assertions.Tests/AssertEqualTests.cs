using System.Collections.Immutable;
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
            Expected: "Hello\n\"World\""
            Actual:   "Hello\tWorld"
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
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equal("Hello", "hello"));
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
    public void DifferentConcreteCollectionTypes_Fails()
    {
        var expected = new List<int> { 1, 2, 3 };
        var actual = new List<long> { 1L, 42L, 3L };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equal(expected, actual), """
            Assert.Equal() assertion failed: Lengths differ.
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
            Assert.Equal() assertion failed: Lengths differ.
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
            Assert.Equal() assertion failed: Lengths differ.
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
            Assert.Equal() assertion failed: Lengths differ.
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
            Assert.Equal() assertion failed: Lengths differ.
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
            Assert.Equal() assertion failed: Lengths differ.
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
}
