using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertEqualByStructureTests
{
    [Fact]
    public void Null_Success()
    {
        AssertionsAssert.Equivalent(expected: null, actual: null);
    }

    [Fact]
    public void Scalar_Success()
    {
        AssertionsAssert.Equivalent(42, 42L);
        AssertionsAssert.Equivalent("value", "value");
    }

    [Fact]
    public void DifferentTypesWithSamePublicProperties_Success()
    {
        var expected = new ExpectedPerson { Name = "Alice", Age = 42 };
        var actual = new ActualPerson { Name = "Alice", Age = 42 };

        AssertionsAssert.Equivalent(expected, actual);
    }

    [Fact]
    public void PublicFieldsAndProperties_Success()
    {
        var expected = new FieldPerson { Name = "Alice", Age = 42 };
        var actual = new ActualPerson { Name = "Alice", Age = 42 };

        AssertionsAssert.Equivalent(expected, actual);
    }

    [Fact]
    public void PrivateMembersAreIgnored_Success()
    {
        var expected = new PersonWithPrivateState("Alice", "expected secret");
        var actual = new PersonWithPrivateState("Alice", "actual secret");

        AssertionsAssert.Equivalent(expected, actual);
    }

    [Fact]
    public void NestedObjects_Success()
    {
        var expected = new ExpectedPersonWithAddress
        {
            Name = "Alice",
            Address = new ExpectedAddress { City = "Paris", ZipCode = 75000 },
        };
        var actual = new ActualPersonWithAddress
        {
            Name = "Alice",
            Address = new ActualAddress { City = "Paris", ZipCode = 75000L },
        };

        AssertionsAssert.Equivalent(expected, actual);
    }

    [Fact]
    public void Collections_Success()
    {
        var expected = new PersonWithScores { Name = "Alice", Scores = new[] { 1, 2, 3 } };
        var actual = new PersonWithScores { Name = "Alice", Scores = new[] { 1L, 2L, 3L } };

        AssertionsAssert.Equivalent(expected, actual);
    }

    [Fact]
    public void Equivalent_Success()
    {
        var expected = new ExpectedPerson { Name = "Alice", Age = 42 };
        var actual = new ActualPerson { Name = "Alice", Age = 42 };

        AssertionsAssert.Equivalent(expected, actual);
    }

    [Fact]
    public void Equivalent_FailsWhenCollectionOrderDiffersByDefault()
    {
        var expected = new PersonWithScores { Name = "Alice", Scores = new[] { 1, 2, 3 } };
        var actual = new PersonWithScores { Name = "Alice", Scores = new[] { 3, 2, 1 } };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $.Scores[0]
            Reason: Values differ.
            Expected: 1
            Actual:   3
            """);
    }

    [Fact]
    public void Equivalent_IgnoresCollectionOrderWhenConfigured()
    {
        var expected = new PersonWithScores { Name = "Alice", Scores = new[] { 1, 2, 3 } };
        var actual = new PersonWithScores { Name = "Alice", Scores = new[] { 3, 2, 1 } };

        AssertionsAssert.Equivalent(expected, actual, new EquivalentOptions { IgnoreCollectionOrder = true });
    }

    [Fact]
    public void Equivalent_IgnoresCollectionOrderWithSharedCyclicReferences()
    {
        var expectedShared = new GraphNode { Name = "shared" };
        expectedShared.Next = expectedShared;
        var expectedFirst = new GraphNode { Name = "first", Next = expectedShared };
        var expectedSecond = new GraphNode { Name = "second", Next = expectedShared };

        var actualShared = new GraphNode { Name = "shared" };
        actualShared.Next = actualShared;
        var actualFirst = new GraphNode { Name = "first", Next = actualShared };
        var actualSecond = new GraphNode { Name = "second", Next = actualShared };

        AssertionsAssert.Equivalent(new[] { expectedFirst, expectedSecond }, new[] { actualSecond, actualFirst }, new EquivalentOptions { IgnoreCollectionOrder = true });
    }

    [Fact]
    public void Equivalent_IgnoresCollectionOrderAfterRejectingCandidatesWithNestedDifferences()
    {
        // The first candidate for each expected item differs deep inside the graph. The path segments of those
        // rejected attempts must not leak into the path reported for a later failure.
        var expected = new[]
        {
            new GraphNode { Name = "a", Next = new GraphNode { Name = "a-next" } },
            new GraphNode { Name = "b", Next = new GraphNode { Name = "b-next" } },
        };

        var actual = new[]
        {
            new GraphNode { Name = "b", Next = new GraphNode { Name = "b-next" } },
            new GraphNode { Name = "a", Next = new GraphNode { Name = "a-next" } },
        };

        AssertionsAssert.Equivalent(expected, actual, new EquivalentOptions { IgnoreCollectionOrder = true });
    }

    [Fact]
    public void Equivalent_FailsWithIgnoredCollectionOrderAfterRejectingCandidates()
    {
        var expected = new[]
        {
            new GraphNode { Name = "a", Next = new GraphNode { Name = "a-next" } },
            new GraphNode { Name = "b", Next = new GraphNode { Name = "b-next" } },
        };

        var actual = new[]
        {
            new GraphNode { Name = "b", Next = new GraphNode { Name = "b-next" } },
            new GraphNode { Name = "a", Next = new GraphNode { Name = "different" } },
        };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual, new EquivalentOptions { IgnoreCollectionOrder = true }), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $[0]
            Reason: Actual collection is missing an equivalent item.
            Expected: Meziantou.Framework.Assertions.Tests.AssertEqualByStructureTests+GraphNode
            Actual:   <missing>
            """);
    }

    [Fact]
    public void Equivalent_FailsWithIgnoredCollectionOrderAndSharedCyclicReferences()
    {
        var expectedShared = new GraphNode { Name = "shared" };
        expectedShared.Next = expectedShared;
        var expected = new[] { new GraphNode { Name = "first", Next = expectedShared } };

        var actualShared = new GraphNode { Name = "shared" };
        actualShared.Next = actualShared;
        var actual = new[] { new GraphNode { Name = "second", Next = actualShared } };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual, new EquivalentOptions { IgnoreCollectionOrder = true }), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $[0]
            Reason: Actual collection is missing an equivalent item.
            Expected: Meziantou.Framework.Assertions.Tests.AssertEqualByStructureTests+GraphNode
            Actual:   <missing>
            """);
    }

    [Fact]
    public void Equivalent_FailsWhenMemberNameCaseDiffersByDefault()
    {
        var expected = new ZipCodeContainerExpected { ZipCode = 75000 };
        var actual = new ZipCodeContainerActual { Zipcode = 75000 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $.ZipCode
            Reason: Actual member is missing.
            Expected: 75000
            Actual:   <missing>
            """);
    }

    [Fact]
    public void Equivalent_IgnoresMemberNameCaseWhenConfigured()
    {
        var expected = new ZipCodeContainerExpected { ZipCode = 75000 };
        var actual = new ZipCodeContainerActual { Zipcode = 75000 };

        AssertionsAssert.Equivalent(expected, actual, new EquivalentOptions { IgnoreMemberNameCase = true });
    }

    [Fact]
    public void Equivalent_FailsWhenStringCaseDiffersByDefault()
    {
        var expected = new ExpectedPerson { Name = "Alice", Age = 42 };
        var actual = new ActualPerson { Name = "alice", Age = 42 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $.Name
            Reason: Values differ.
            Expected: "Alice"
            Actual:   "alice"
            """);
    }

    [Fact]
    public void Equivalent_IgnoresStringCaseWhenConfigured()
    {
        var expected = new ExpectedPerson { Name = "Alice", Age = 42 };
        var actual = new ActualPerson { Name = "alice", Age = 42 };

        AssertionsAssert.Equivalent(expected, actual, new EquivalentOptions { IgnoreStringCase = true });
    }

    [Fact]
    public void Cycles_Success()
    {
        var expected = new Node { Name = "root" };
        expected.Next = expected;
        var actual = new Node { Name = "root" };
        actual.Next = actual;

        AssertionsAssert.Equivalent(expected, actual);
    }

    [Fact]
    public void FailsWhenPropertyValueDiffers()
    {
        var expected = new ExpectedPerson { Name = "Alice", Age = 42 };
        var actual = new ActualPerson { Name = "Bob", Age = 42 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $.Name
            Reason: Values differ.
            Expected: "Alice"
            Actual:   "Bob"
            """);
    }

    [Fact]
    public void FailsWhenNestedPropertyValueDiffers()
    {
        var expected = new ExpectedPersonWithAddress
        {
            Name = "Alice",
            Address = new ExpectedAddress { City = "Paris", ZipCode = 75000 },
        };
        var actual = new ActualPersonWithAddress
        {
            Name = "Alice",
            Address = new ActualAddress { City = "Paris", ZipCode = 69000 },
        };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual, "custom message"), """
            Assert.Equivalent() assertion failed.
            Message: custom message
            Expected expression: expected
            Actual expression:   actual
            Path: $.Address.ZipCode
            Reason: Values differ.
            Expected: 75000
            Actual:   69000
            """);
    }

    [Fact]
    public void FailsWhenActualMemberIsMissing()
    {
        var expected = new ExpectedPerson { Name = "Alice", Age = 42 };
        var actual = new PersonWithNameOnly { Name = "Alice" };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $.Age
            Reason: Actual member is missing.
            Expected: 42
            Actual:   <missing>
            """);
    }

    [Fact]
    public void FailsWhenActualMemberIsUnexpected()
    {
        var expected = new PersonWithNameOnly { Name = "Alice" };
        var actual = new ActualPerson { Name = "Alice", Age = 42 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $.Age
            Reason: Actual member is unexpected.
            Expected: <missing>
            Actual:   42
            """);
    }

    [Fact]
    public void FailsWhenCollectionItemDiffers()
    {
        var expected = new PersonWithScores { Name = "Alice", Scores = new[] { 1, 2, 3 } };
        var actual = new PersonWithScores { Name = "Alice", Scores = new[] { 1, 42, 3 } };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $.Scores[1]
            Reason: Values differ.
            Expected: 2
            Actual:   42
            """);
    }

    [Fact]
    public void FailsWhenActualCollectionIsMissingItem()
    {
        var expected = new PersonWithScores { Name = "Alice", Scores = new[] { 1, 2, 3 } };
        var actual = new PersonWithScores { Name = "Alice", Scores = new[] { 1, 2 } };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $.Scores[2]
            Reason: Actual collection is missing an item.
            Expected: 3
            Actual:   <missing>
            """);
    }

    [Fact]
    public void FailsWhenActualCollectionHasUnexpectedItem()
    {
        var expected = new PersonWithScores { Name = "Alice", Scores = new[] { 1, 2 } };
        var actual = new PersonWithScores { Name = "Alice", Scores = new[] { 1, 2, 3 } };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $.Scores[2]
            Reason: Actual collection contains an unexpected item.
            Expected: <missing>
            Actual:   3
            """);
    }

    [Fact]
    public void FailsWhenNullabilityDiffers()
    {
        var expected = new NullablePerson { Name = null };
        var actual = new NullablePerson { Name = "Alice" };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $.Name
            Reason: Values differ.
            Expected: <null>
            Actual:   "Alice"
            """);
    }

    [Fact]
    public void NotEquivalent_Success()
    {
        var expected = new ExpectedPerson { Name = "Alice", Age = 42 };
        var actual = new ActualPerson { Name = "Alice", Age = 43 };

        AssertionsAssert.NotEquivalent(expected, actual);
    }

    [Fact]
    public void NotEquivalent_Fails()
    {
        var expected = new ExpectedPerson { Name = "Alice", Age = 42 };
        var actual = new ActualPerson { Name = "Alice", Age = 42 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEquivalent(expected, actual), """
            Assert.NotEquivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected: Meziantou.Framework.Assertions.Tests.AssertEqualByStructureTests+ExpectedPerson
            Actual:       Meziantou.Framework.Assertions.Tests.AssertEqualByStructureTests+ActualPerson
            """);
    }

    [Fact]
    public void NotEquivalent_IgnoresCollectionOrderWhenConfigured()
    {
        var expected = new PersonWithScores { Name = "Alice", Scores = new[] { 1, 2, 3 } };
        var actual = new PersonWithScores { Name = "Alice", Scores = new[] { 3, 2, 1 } };

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEquivalent(expected, actual, new EquivalentOptions { IgnoreCollectionOrder = true }), """
            Assert.NotEquivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected: Meziantou.Framework.Assertions.Tests.AssertEqualByStructureTests+PersonWithScores
            Actual:       Meziantou.Framework.Assertions.Tests.AssertEqualByStructureTests+PersonWithScores
            """);
    }

    [Fact]
    public void Equivalent_NumbersWithoutTypeCode_Success()
    {
        AssertionsAssert.Equivalent((Int128)1, (Int128)1);
        AssertionsAssert.Equivalent((UInt128)1, (UInt128)1);
        AssertionsAssert.Equivalent((Half)1, (Half)1);
        AssertionsAssert.Equivalent(new System.Numerics.BigInteger(3), new System.Numerics.BigInteger(3));
        AssertionsAssert.Equivalent(new { X = (Int128)1 }, new { X = 1 });
    }

    [Fact]
    public void Equivalent_FailsWhenInt128Differs()
    {
        var expected = (Int128)1;
        var actual = (Int128)2;

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $
            Reason: Values differ.
            Expected: 1
            Actual:   2
            """);
    }

    [Fact]
    public void Equivalent_FailsWhenUInt128Differs()
    {
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equivalent((UInt128)1, (UInt128)2));
    }

    [Fact]
    public void Equivalent_FailsWhenHalfMemberDiffers()
    {
        var expected = new { X = (Half)1 };
        var actual = new { X = (Half)2 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $.X
            Reason: Values differ.
            Expected: 1
            Actual:   2
            """);
    }

    [Fact]
    public void Equivalent_FailsWhenBigIntegerDiffers()
    {
        // 3 and 5 agree on every public property of BigInteger (IsZero, IsOne, IsEven, Sign, IsPowerOfTwo).
        var expected = new System.Numerics.BigInteger(3);
        var actual = new System.Numerics.BigInteger(5);

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $
            Reason: Values differ.
            Expected: 3
            Actual:   5
            """);
        AssertionsAssert.NotEquivalent(expected, actual);
    }

    [Fact]
    public void Equivalent_ValuesWithoutPublicMembersUseEquality()
    {
        AssertionsAssert.Equivalent(new OpaqueValue(1), new OpaqueValue(1));
        AssertionsAssert.NotEquivalent(new object(), new object());

        var expected = new { Value = new OpaqueValue(1) };
        var actual = new { Value = new OpaqueValue(2) };
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $.Value
            Reason: Values differ.
            Expected: Opaque(1)
            Actual:   Opaque(2)
            """);
        AssertionsAssert.NotEquivalent(expected, actual);
    }

    [Fact]
    public void Equivalent_Memory_Success()
    {
        AssertionsAssert.Equivalent(new { D = new byte[] { 1 }.AsMemory() }, new { D = new byte[] { 1 }.AsMemory() });
        AssertionsAssert.Equivalent(new ReadOnlyMemory<int>([1, 2]), new ReadOnlyMemory<int>([1, 2]));
        AssertionsAssert.Equivalent(new[] { 1, 2 }, new Memory<long>([1, 2]));
        AssertionsAssert.NotEquivalent(new ReadOnlyMemory<int>([1, 2]), new ReadOnlyMemory<int>([1, 3]));
    }

    [Fact]
    public void Equivalent_FailsWhenMemoryContentDiffers()
    {
        var expected = new { D = new byte[] { 1, 2 }.AsMemory() };
        var actual = new { D = new byte[] { 1, 3 }.AsMemory() };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $.D[1]
            Reason: Values differ.
            Expected: 2
            Actual:   3
            """);
    }

    [Fact]
    public void Equivalent_ReadOnlySequenceComparesContent()
    {
        var expected = new { Data = new System.Buffers.ReadOnlySequence<byte>(new byte[] { 1, 2 }) };

        AssertionsAssert.Equivalent(expected, new { Data = new System.Buffers.ReadOnlySequence<byte>(new byte[] { 1, 2 }) });
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, new { Data = new System.Buffers.ReadOnlySequence<byte>(new byte[] { 1, 3 }) }), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   new { Data = new System.Buffers.ReadOnlySequence<byte>(new byte[] { 1, 3 }) }
            Path: $.Data[1]
            Reason: Values differ.
            Expected: 2
            Actual:   3
            """);
    }

    [Fact]
    public void Equivalent_MembersThatCannotBeReadAreIgnored()
    {
        AssertionsAssert.Equivalent(new SpanHolder("Alice", [1]), new SpanHolder("Alice", [2]));
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(new SpanHolder("Alice", [1]), new SpanHolder("Bob", [1])), """
            Assert.Equivalent() assertion failed.
            Expected expression: new SpanHolder("Alice", [1])
            Actual expression:   new SpanHolder("Bob", [1])
            Path: $.Name
            Reason: Values differ.
            Expected: "Alice"
            Actual:   "Bob"
            """);
    }

    [Fact]
    public void Equivalent_RefReturningPropertiesAreCompared()
    {
        AssertionsAssert.Equivalent(new RefValueHolder(1), new RefValueHolder(1));
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(new RefValueHolder(1), new RefValueHolder(2)), """
            Assert.Equivalent() assertion failed.
            Expected expression: new RefValueHolder(1)
            Actual expression:   new RefValueHolder(2)
            Path: $.Value
            Reason: Values differ.
            Expected: 1
            Actual:   2
            """);
    }

    [Fact]
    public void Equivalent_DictionariesIgnoreEntryOrder()
    {
        var expected = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var actual = new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 };

        AssertionsAssert.Equivalent(expected, actual);
        AssertionsAssert.Equivalent(new { Values = expected }, new { Values = actual });
        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEquivalent(expected, actual), """
            Assert.NotEquivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected: [["a", 1], ["b", 2]]
            Actual:       [["b", 2], ["a", 1]]
            """);
    }

    [Fact]
    public void Equivalent_DictionariesOfDifferentTypes_Success()
    {
        AssertionsAssert.Equivalent(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }, new SortedDictionary<string, long>(StringComparer.Ordinal) { ["b"] = 2, ["a"] = 1 });
        AssertionsAssert.Equivalent(new Dictionary<int, string> { [1] = "a", [2] = "b" }, new Dictionary<long, string> { [2] = "b", [1] = "a" });
        AssertionsAssert.Equivalent(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }, new System.Collections.Hashtable { ["b"] = 2, ["a"] = 1 });
        AssertionsAssert.Equivalent(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }, new ReadOnlyOnlyDictionary(new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 }));
        AssertionsAssert.Equivalent(new Dictionary<(int, string), int> { [(1, "a")] = 1, [(2, "b")] = 2 }, new Dictionary<(int, string), int> { [(2, "b")] = 2, [(1, "a")] = 1 });
    }

    [Fact]
    public void Equivalent_FailsWhenDictionaryValueDiffers()
    {
        var expected = new { Values = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 } };
        var actual = new { Values = new Dictionary<string, int> { ["b"] = 3, ["a"] = 1 } };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $.Values["b"]
            Reason: Values differ.
            Expected: 2
            Actual:   3
            """);
        AssertionsAssert.NotEquivalent(expected, actual);
    }

    [Fact]
    public void Equivalent_FailsWhenDictionaryIsMissingKey()
    {
        var expected = new Dictionary<int, string> { [1] = "a", [2] = "b" };
        var actual = new Dictionary<int, string> { [2] = "b" };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $[1]
            Reason: Actual dictionary is missing a key.
            Expected: "a"
            Actual:   <missing>
            """);
    }

    [Fact]
    public void Equivalent_FailsWhenDictionaryHasUnexpectedKey()
    {
        var expected = new Dictionary<string, int> { ["b"] = 2 };
        var actual = new Dictionary<string, int> { ["a\""] = 1, ["b"] = 2 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $["a\""]
            Reason: Actual dictionary contains an unexpected key.
            Expected: <missing>
            Actual:   1
            """);
    }

    [Fact]
    public void Equivalent_DictionaryKeysUseStringCaseOption()
    {
        var expected = new Dictionary<string, int> { ["A"] = 1, ["b"] = 2 };
        var actual = new Dictionary<string, int> { ["B"] = 2, ["a"] = 1 };

        AssertionsAssert.Equivalent(expected, actual, new EquivalentOptions { IgnoreStringCase = true });
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $["A"]
            Reason: Actual dictionary is missing a key.
            Expected: 1
            Actual:   <missing>
            """);
    }

    [Fact]
    public void Equivalent_SetsIgnoreOrder()
    {
        var descending = Comparer<int>.Create((x, y) => y.CompareTo(x));

        AssertionsAssert.Equivalent(new[] { 1, 2, 3 }, new SortedSet<int>([1, 2, 3], descending));
        AssertionsAssert.Equivalent(new SortedSet<int>([1, 2, 3], descending), new List<long> { 1, 2, 3 });
        AssertionsAssert.Equivalent(new { Tags = new SortedSet<string>(["a", "b"], StringComparer.Ordinal) }, new { Tags = new[] { "b", "a" } });
        AssertionsAssert.NotEquivalent(new[] { 1, 2, 3 }, new SortedSet<int>([1, 2, 4], descending));
    }

    [Fact]
    public void Equivalent_FailsWhenSetItemIsMissing()
    {
        var expected = new[] { 1, 2, 3 };
        var actual = new SortedSet<int>([1, 2, 4], Comparer<int>.Create((x, y) => y.CompareTo(x)));

        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $[2]
            Reason: Actual collection is missing an equivalent item.
            Expected: 3
            Actual:   <missing>
            """);
    }

    [Fact]
    public void Equivalent_IgnoresCollectionOrderWithDuplicateLeaves()
    {
        var options = new EquivalentOptions { IgnoreCollectionOrder = true };
        AssertionsAssert.Equivalent(new[] { 1, 1, 2 }, new[] { 1, 2, 1 }, options);
        AssertionsAssert.Equivalent(new[] { 1, 1, 2 }, new object[] { 1L, 2, 1 }, options);

        var expected = new[] { 1, 1, 2 };
        var actual = new[] { 1, 2, 2 };
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual, options), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $[1]
            Reason: Actual collection is missing an equivalent item.
            Expected: 1
            Actual:   <missing>
            """);
    }

    [Fact]
    public void Equivalent_NotEquivalent_MemberNameCaseComplement()
    {
        var expected = new ZipCodeContainerExpected { ZipCode = 75000 };
        var actual = new ZipCodeContainerActual { Zipcode = 75000 };
        var options = new EquivalentOptions { IgnoreMemberNameCase = true };

        AssertionsAssert.NotEquivalent(expected, actual);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equivalent(expected, actual));
        AssertionsAssert.Equivalent(expected, actual, options);
        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEquivalent(expected, actual, options), """
            Assert.NotEquivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected: Meziantou.Framework.Assertions.Tests.AssertEqualByStructureTests+ZipCodeContainerExpected
            Actual:       Meziantou.Framework.Assertions.Tests.AssertEqualByStructureTests+ZipCodeContainerActual
            """);
    }

    [Fact]
    public void Equivalent_NotEquivalent_StringCaseComplement()
    {
        var expected = new ExpectedPerson { Name = "Alice", Age = 42 };
        var actual = new ActualPerson { Name = "alice", Age = 42 };
        var options = new EquivalentOptions { IgnoreStringCase = true };

        AssertionsAssert.NotEquivalent(expected, actual);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equivalent(expected, actual));
        AssertionsAssert.Equivalent(expected, actual, options);
        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEquivalent(expected, actual, options), """
            Assert.NotEquivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected: Meziantou.Framework.Assertions.Tests.AssertEqualByStructureTests+ExpectedPerson
            Actual:       Meziantou.Framework.Assertions.Tests.AssertEqualByStructureTests+ActualPerson
            """);

        AssertionsAssert.NotEquivalent("Alice", "alice");
        AssertionsAssert.Equivalent("Alice", "alice", options);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEquivalent("Alice", "alice", options));
    }

    [Fact]
    public void Equivalent_JsonElementComparesContent()
    {
        AssertionsAssert.Equivalent(ParseJson("""{"a":1,"b":[1,"x",null]}"""), ParseJson("""{"b":[1.0,"x",null],"a":1}"""));
        AssertionsAssert.NotEquivalent(ParseJson("1"), ParseJson("2"));
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(ParseJson("1"), ParseJson("2")), """
            Assert.Equivalent() assertion failed.
            Expected expression: ParseJson("1")
            Actual expression:   ParseJson("2")
            Path: $
            Reason: Values differ.
            Expected: 1
            Actual:   2
            """);

        var expected = ParseJson("""{"a":{"b":["x","y"]}}""");
        var actual = ParseJson("""{"a":{"b":["x","z"]}}""");
        AssertionsAssert.NotEquivalent(expected, actual);
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $["a"]["b"][1]
            Reason: Values differ.
            Expected: "y"
            Actual:   "z"
            """);
    }

    [Fact]
    public void Equivalent_JsonElementDifferentKinds()
    {
        AssertionsAssert.NotEquivalent(ParseJson("[1]"), ParseJson("""{"0":1}"""));
        AssertionsAssert.NotEquivalent(ParseJson("true"), ParseJson("1"));
        AssertionsAssert.NotEquivalent(ParseJson("null"), ParseJson("0"));
        AssertionsAssert.NotEquivalent(ParseJson("\"1\""), ParseJson("1"));
        AssertionsAssert.NotEquivalent(ParseJson("""{"a":null}"""), ParseJson("{}"));
        AssertionsAssert.NotEquivalent(default(JsonElement), ParseJson("null"));
        AssertionsAssert.Equivalent(default(JsonElement), default(JsonElement));
        AssertionsAssert.Equivalent(ParseJson("null"), ParseJson("null"));
    }

    [Fact]
    public void Equivalent_JsonNodeComparesContent()
    {
        AssertionsAssert.Equivalent(JsonNode.Parse("""{"a":1,"b":[true,"x",null]}"""), JsonNode.Parse("""{"b":[true,"x",null],"a":1.0}"""));
        AssertionsAssert.NotEquivalent(JsonNode.Parse("1"), JsonNode.Parse("2"));
        AssertionsAssert.NotEquivalent(JsonValue.Create(1), JsonValue.Create(2));
        AssertionsAssert.Equivalent(JsonValue.Create(1), JsonNode.Parse("1"));

        var expected = JsonNode.Parse("""{"a":1}""");
        var actual = JsonNode.Parse("""{"a":2}""");
        AssertionsAssert.NotEquivalent(expected, actual);
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $["a"]
            Reason: Values differ.
            Expected: 1
            Actual:   2
            """);
    }

    [Fact]
    public void Equivalent_JsonNodeAndJsonElement()
    {
        AssertionsAssert.Equivalent(JsonNode.Parse("""{"a":[1,"x",null,{"b":false}]}"""), ParseJson("""{"a":[1,"x",null,{"b":false}]}"""));
        AssertionsAssert.Equivalent(new JsonObject { ["a"] = 1, ["b"] = "x" }, ParseJson("""{"b":"x","a":1}"""));
        AssertionsAssert.NotEquivalent(new JsonObject { ["a"] = 1 }, ParseJson("""{"a":2}"""));
        AssertionsAssert.NotEquivalent(JsonNode.Parse("[1]"), ParseJson("[2]"));
    }

    [Fact]
    public void Equivalent_JsonHonorsOptions()
    {
        AssertionsAssert.NotEquivalent(ParseJson("""["a","B"]"""), ParseJson("""["b","A"]"""));
        AssertionsAssert.Equivalent(ParseJson("""["a","B"]"""), ParseJson("""["b","A"]"""), new EquivalentOptions { IgnoreCollectionOrder = true, IgnoreStringCase = true });
        AssertionsAssert.Equivalent(JsonNode.Parse("""{"a":"X"}"""), JsonNode.Parse("""{"A":"x"}"""), new EquivalentOptions { IgnoreStringCase = true });
        AssertionsAssert.NotEquivalent(JsonNode.Parse("[1,2]"), JsonNode.Parse("[2,1]"));
        AssertionsAssert.Equivalent(JsonNode.Parse("[1,2]"), JsonNode.Parse("[2,1]"), new EquivalentOptions { IgnoreCollectionOrder = true });
    }

    [Fact]
    public void Equivalent_JsonDocumentComparesContent()
    {
        using var expected = JsonDocument.Parse("""{"a":1}""");
        using var actual = JsonDocument.Parse("""{"a":2}""");

        AssertionsAssert.NotEquivalent(expected, actual);
    }

    [Fact]
    public void Equivalent_StringBuilderComparesContent()
    {
        AssertionsAssert.Equivalent(new StringBuilder("abc", capacity: 3), new StringBuilder("abc", capacity: 100));
        AssertionsAssert.NotEquivalent(new StringBuilder("abc"), new StringBuilder("xyz"));
        AssertionsAssert.Equivalent(new StringBuilder("abc"), new StringBuilder("ABC"), new EquivalentOptions { IgnoreStringCase = true });
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(new { Text = new StringBuilder("abc") }, new { Text = new StringBuilder("xyz") }), """
            Assert.Equivalent() assertion failed.
            Expected expression: new { Text = new StringBuilder("abc") }
            Actual expression:   new { Text = new StringBuilder("xyz") }
            Path: $.Text
            Reason: Values differ.
            Expected: "abc"
            Actual:   "xyz"
            """);
    }

    [Fact]
    public void Equivalent_XmlComparesContent()
    {
        AssertionsAssert.Equivalent(XElement.Parse("<a x='1'><b>text</b></a>"), XElement.Parse("<a x='1'><b>text</b></a>"));
        AssertionsAssert.NotEquivalent(XElement.Parse("<a x='1'><b/></a>"), XElement.Parse("<a x='1'><c/></a>"));
        AssertionsAssert.NotEquivalent(XElement.Parse("<a x='1'/>"), XElement.Parse("<a x='2'/>"));
        AssertionsAssert.Equivalent(new XAttribute("x", "1"), new XAttribute("x", "1"));
        AssertionsAssert.NotEquivalent(new XAttribute("x", "1"), new XAttribute("y", "1"));
    }

    [Fact]
    public void Equivalent_RegexComparesPatternAndOptions()
    {
        AssertionsAssert.Equivalent(new Regex("a+", RegexOptions.None, Regex.InfiniteMatchTimeout), new Regex("a+", RegexOptions.None, Regex.InfiniteMatchTimeout));
        AssertionsAssert.NotEquivalent(new Regex("a", RegexOptions.None, Regex.InfiniteMatchTimeout), new Regex("b", RegexOptions.None, Regex.InfiniteMatchTimeout));
        AssertionsAssert.NotEquivalent(new Regex("a", RegexOptions.None, Regex.InfiniteMatchTimeout), new Regex("a", RegexOptions.IgnoreCase, Regex.InfiniteMatchTimeout));
    }

    [Fact]
    public void Equivalent_FileSystemInfoComparesFullName()
    {
        var directory = Path.GetTempPath();

        AssertionsAssert.Equivalent(new DirectoryInfo(directory), new DirectoryInfo(directory));
        AssertionsAssert.Equivalent(new FileInfo(Path.Combine(directory, "a.txt")), new FileInfo(Path.Combine(directory, "a.txt")));
        AssertionsAssert.NotEquivalent(new FileInfo(Path.Combine(directory, "a.txt")), new FileInfo(Path.Combine(directory, "b.txt")));
        AssertionsAssert.NotEquivalent(new FileInfo(Path.Combine(directory, "a")), new DirectoryInfo(Path.Combine(directory, "a")));
    }

    [Fact]
    public void Equivalent_FrameworkTypesUseTheirEquality()
    {
        // MediaTypeHeaderValue ignores the case of the media type, while its MediaType property keeps it.
        AssertionsAssert.Equivalent(new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain"), new System.Net.Http.Headers.MediaTypeHeaderValue("TEXT/plain"));
        AssertionsAssert.NotEquivalent(new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain"), new System.Net.Http.Headers.MediaTypeHeaderValue("text/html"));
        AssertionsAssert.Equivalent(System.Xml.Linq.XName.Get("a"), System.Xml.Linq.XName.Get("a"));
        AssertionsAssert.NotEquivalent(System.Xml.Linq.XName.Get("a"), System.Xml.Linq.XName.Get("b"));
        AssertionsAssert.NotEquivalent(new System.Numerics.Vector<int>(1), new System.Numerics.Vector<int>(2));
    }

    [Fact]
    public void Equivalent_UserTypesWithEqualityAreStillWalked()
    {
        // The equality of these types is weaker than their structure, or compares the list by reference.
        AssertionsAssert.NotEquivalent(new EntityWithId(1, "Alice"), new EntityWithId(1, "Bob"));
        AssertionsAssert.Equivalent(new PersonRecord("Alice", [1, 2]), new PersonRecord("Alice", [1, 2]));
        AssertionsAssert.Equivalent(new PersonRecord("Alice", [1, 2]), new PersonRecord("ALICE", [1, 2]), new EquivalentOptions { IgnoreStringCase = true });
        AssertionsAssert.Equivalent((1, new List<int> { 1 }), (1, new List<int> { 1 }));
        AssertionsAssert.NotEquivalent((1, new List<int> { 1 }), (1, new List<int> { 2 }));
    }

    [Fact]
    public void Equivalent_ValueLikeFrameworkTypesAreLeaves()
    {
        AssertionsAssert.Equivalent(System.Net.IPAddress.Parse("10.0.0.1"), System.Net.IPAddress.Parse("10.0.0.1"));
        AssertionsAssert.Equivalent(System.Net.IPAddress.Parse("::1"), System.Net.IPAddress.Parse("::1"));
        AssertionsAssert.NotEquivalent(System.Net.IPAddress.Parse("10.0.0.1"), System.Net.IPAddress.Parse("10.0.0.2"));
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(new { Address = System.Net.IPAddress.Parse("10.0.0.1") }, new { Address = System.Net.IPAddress.Parse("10.0.0.2") }), """
            Assert.Equivalent() assertion failed.
            Expected expression: new { Address = System.Net.IPAddress.Parse("10.0.0.1") }
            Actual expression:   new { Address = System.Net.IPAddress.Parse("10.0.0.2") }
            Path: $.Address
            Reason: Values differ.
            Expected: 10.0.0.1
            Actual:   10.0.0.2
            """);

        AssertionsAssert.Equivalent(new { T = typeof(int) }, new { T = typeof(int) });
        AssertionsAssert.NotEquivalent(new { T = typeof(int) }, new { T = typeof(string) });
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.Equivalent(new { T = typeof(int) }, new { T = typeof(string) }));
        AssertionsAssert.NotEquivalent(typeof(string).GetMethod(nameof(string.Trim), Type.EmptyTypes), typeof(string).GetMethod(nameof(string.TrimEnd), Type.EmptyTypes));
        AssertionsAssert.Equivalent(new Version(1, 2), new Version(1, 2));
        AssertionsAssert.NotEquivalent(new Version(1, 2), new Version(1, 2, 0));
        AssertionsAssert.Equivalent(CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo(string.Empty));
    }

    [Fact]
    public void Equivalent_ThrowingGettersAreComparedByException()
    {
        AssertionsAssert.Equivalent(new ThrowingGetter(shouldThrow: true), new ThrowingGetter(shouldThrow: true));
        AssertionsAssert.NotEquivalent(new ThrowingGetter(shouldThrow: true), new ThrowingGetter(shouldThrow: false));
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(new ThrowingGetter(shouldThrow: true), new ThrowingGetter(shouldThrow: false)), """
            Assert.Equivalent() assertion failed.
            Expected expression: new ThrowingGetter(shouldThrow: true)
            Actual expression:   new ThrowingGetter(shouldThrow: false)
            Path: $.Value
            Reason: Member getter threw an exception.
            Expected: <threw System.InvalidOperationException>
            Actual:   1
            """);
    }

    [Fact]
    [SuppressMessage("Performance", "CA1814:Prefer jagged arrays over multidimensional", Justification = "The test is about multi-dimensional arrays")]
    public void Equivalent_MultiDimensionalArraysCompareDimensions()
    {
        AssertionsAssert.Equivalent(new int[,] { { 1, 2 }, { 3, 4 } }, new long[,] { { 1, 2 }, { 3, 4 } });
        AssertionsAssert.NotEquivalent(new int[1, 2], new int[2, 1]);
        AssertionsAssert.NotEquivalent(new int[2], new int[1, 2]);
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(new int[1, 2], new int[2, 1]), """
            Assert.Equivalent() assertion failed.
            Expected expression: new int[1, 2]
            Actual expression:   new int[2, 1]
            Path: $
            Reason: Array dimensions differ: expected 1x2, actual 2x1.
            Expected: [0, 0]
            Actual:   [0, 0]
            Note: The values differ but are formatted identically.
            """);

        var expected = new { Values = new int[,] { { 1, 2 }, { 3, 4 } } };
        var actual = new { Values = new int[,] { { 1, 2 }, { 3, 5 } } };
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, actual), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $.Values[1,1]
            Reason: Values differ.
            Expected: 4
            Actual:   5
            """);

        var options = new EquivalentOptions { IgnoreCollectionOrder = true };
        AssertionsAssert.Equivalent(new int[,] { { 1, 2 }, { 3, 4 } }, new int[,] { { 4, 3 }, { 2, 1 } }, options);
        AssertionsAssert.NotEquivalent(new int[,] { { 1, 2, 3, 4 } }, new int[,] { { 4, 3 }, { 2, 1 } }, options);
    }

    [Fact]
    public void Equivalent_DictionaryKeysMatchingSeveralKeysArePairedByValue()
    {
        var options = new EquivalentOptions { IgnoreStringCase = true };
        var expected = new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1, ["A"] = 2 };

        AssertionsAssert.Equivalent(expected, new Dictionary<string, int>(StringComparer.Ordinal) { ["A"] = 2, ["a"] = 1 }, options);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.NotEquivalent(expected, new Dictionary<string, int>(StringComparer.Ordinal) { ["A"] = 2, ["a"] = 1 }, options));
        AssertionsAssert.Equivalent(expected, new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 2, ["A"] = 1 }, options);
        AssertionsAssert.NotEquivalent(expected, new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 2, ["A"] = 1 });
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(expected, new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1, ["A"] = 3 }, options), """
            Assert.Equivalent() assertion failed.
            Expected expression: expected
            Actual expression:   new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1, ["A"] = 3 }
            Path: $["A"]
            Reason: Values differ.
            Expected: 2
            Actual:   3
            """);
    }

    [Fact]
    public void Equivalent_IgnoreMemberNameCaseComparesMembersDifferingOnlyByCase()
    {
        var options = new EquivalentOptions { IgnoreMemberNameCase = true };

        AssertionsAssert.Equivalent(new CaseCollidingMembersExpected { Name = "a", name = "b" }, new CaseCollidingMembersActual { Name = "a", name = "b" }, options);
        AssertionsAssert.NotEquivalent(new CaseCollidingMembersExpected { Name = "a", name = "b" }, new CaseCollidingMembersActual { Name = "a", name = "c" }, options);
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(new CaseCollidingMembersExpected { Name = "a", name = "b" }, new CaseCollidingMembersActual { Name = "a", name = "c" }, options), """
            Assert.Equivalent() assertion failed.
            Expected expression: new CaseCollidingMembersExpected { Name = "a", name = "b" }
            Actual expression:   new CaseCollidingMembersActual { Name = "a", name = "c" }
            Path: $.name
            Reason: Values differ.
            Expected: "b"
            Actual:   "c"
            """);
        AssertionTestHelpers.Validate(() => AssertionsAssert.Equivalent(new CaseCollidingMembersExpected { Name = "a", name = "b" }, new PersonWithNameOnly { Name = "a" }, options), """
            Assert.Equivalent() assertion failed.
            Expected expression: new CaseCollidingMembersExpected { Name = "a", name = "b" }
            Actual expression:   new PersonWithNameOnly { Name = "a" }
            Path: $.name
            Reason: Actual member is missing.
            Expected: "b"
            Actual:   <missing>
            """);
    }

    [Fact]
    public void Equivalent_UnorderedDoesNotFormatPathsOfRejectedCandidates()
    {
        // Formatting a key throws, so any path built for a rejected candidate fails the assertion.
        var first = new Dictionary<ThrowingToStringKey, int> { [new ThrowingToStringKey(1)] = 1 };
        var second = new Dictionary<ThrowingToStringKey, int> { [new ThrowingToStringKey(1)] = 2 };
        var options = new EquivalentOptions { IgnoreCollectionOrder = true };

        AssertionsAssert.Equivalent(new[] { first, second }, new[] { second, first }, options);
        AssertionsAssert.NotEquivalent(first, second);
        AssertionsAssert.NotEquivalent(new[] { first }, new[] { second }, options);
    }

    [Fact]
    public void Equivalent_UnorderedRejectsCandidatesWithDifferentLeafMembersCheaply()
    {
        var counter = new StrongBox<int>();
        var expected = Enumerable.Range(0, 200).Select(i => new CountingItem(counter, i, "Name" + i)).ToArray();
        var actual = Enumerable.Range(0, 200).Reverse().Select(i => new CountingItem(counter, i, "NAME" + i)).ToArray();

        AssertionsAssert.Equivalent(expected, actual, new EquivalentOptions { IgnoreCollectionOrder = true, IgnoreStringCase = true });
        AssertionsAssert.True(counter.Value <= 2 * expected.Length, "Payload was read " + counter.Value.ToString(CultureInfo.InvariantCulture) + " times");
    }

    [Fact]
    public void Equivalent_UnorderedSignaturesMatchEquivalentValues()
    {
        var options = new EquivalentOptions { IgnoreCollectionOrder = true };

        AssertionsAssert.Equivalent(new[] { new SignatureItem { Number = 1, Ratio = double.NaN, Optional = null }, new SignatureItem { Number = 2, Ratio = -0.0, Optional = 3 } }, new[] { new SignatureItem { Number = 2, Ratio = 0.0, Optional = 3 }, new SignatureItem { Number = 1, Ratio = double.NaN, Optional = null } }, options);
        AssertionsAssert.Equivalent(new object[] { new { Id = 1, Name = "a" }, new { Id = 2, Name = "b" } }, new object[] { new { Id = 2L, Name = "b" }, new { Id = 1L, Name = "a" } }, options);
        AssertionsAssert.Equivalent(new object[] { new { Id = 1, Name = "a" }, new { Id = 2, Name = "b" } }, new object[] { new { id = 2, name = "B" }, new { id = 1, name = "A" } }, new EquivalentOptions { IgnoreCollectionOrder = true, IgnoreMemberNameCase = true, IgnoreStringCase = true });
        AssertionsAssert.NotEquivalent(new[] { new SignatureItem { Number = 1 } }, new[] { new SignatureItem { Number = 2 } }, options);
    }

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class OpaqueValue(int value) : IEquatable<OpaqueValue>
    {
        private readonly int _value = value;

        public bool Equals([NotNullWhen(true)] OpaqueValue? other) => other is not null && _value == other._value;

        public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as OpaqueValue);

        public override int GetHashCode() => _value;

        public override string ToString() => "Opaque(" + _value.ToString(CultureInfo.InvariantCulture) + ")";
    }

    private sealed class SpanHolder(string name, byte[] data)
    {
        public string Name { get; } = name;

        public ReadOnlySpan<byte> Data => data;
    }

    private sealed class RefValueHolder(int value)
    {
        private int _value = value;

        public ref int Value => ref _value;
    }

    private sealed class ReadOnlyOnlyDictionary(Dictionary<string, int> inner) : IReadOnlyDictionary<string, int>
    {
        public int this[string key] => inner[key];

        public IEnumerable<string> Keys => inner.Keys;

        public IEnumerable<int> Values => inner.Values;

        public int Count => inner.Count;

        public bool ContainsKey(string key) => inner.ContainsKey(key);

        public IEnumerator<KeyValuePair<string, int>> GetEnumerator() => inner.GetEnumerator();

        public bool TryGetValue(string key, [MaybeNullWhen(false)] out int value) => inner.TryGetValue(key, out value);

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class ExpectedPerson
    {
        public string? Name { get; set; }
        public int Age { get; set; }
    }

    private sealed class ActualPerson
    {
        public string? Name { get; set; }
        public long Age { get; set; }
    }

    private sealed class FieldPerson
    {
        public string? Name;
        public int Age;
    }

    private sealed class PersonWithNameOnly
    {
        public string? Name { get; set; }
    }

    private sealed class PersonWithPrivateState(string name, string secret)
    {
        private readonly string _secret = secret;

        public string Name { get; } = name;

        public override string ToString()
        {
            return Name + ":" + _secret;
        }
    }

    private sealed class ExpectedPersonWithAddress
    {
        public string? Name { get; set; }
        public ExpectedAddress? Address { get; set; }
    }

    private sealed class ActualPersonWithAddress
    {
        public string? Name { get; set; }
        public ActualAddress? Address { get; set; }
    }

    private sealed class ExpectedAddress
    {
        public string? City { get; set; }
        public int ZipCode { get; set; }
    }

    private sealed class ActualAddress
    {
        public string? City { get; set; }
        public long ZipCode { get; set; }
    }

    private sealed class ZipCodeContainerExpected
    {
        public int ZipCode { get; set; }
    }

    private sealed class ZipCodeContainerActual
    {
        public int Zipcode { get; set; }
    }

    private sealed class PersonWithScores
    {
        public string? Name { get; set; }
        public System.Collections.IEnumerable? Scores { get; set; }
    }

    private sealed class NullablePerson
    {
        public string? Name { get; set; }
    }

    private sealed class GraphNode
    {
        public string? Name { get; set; }
        public GraphNode? Next { get; set; }
    }

    private sealed class Node
    {
        public string? Name { get; set; }
        public Node? Next { get; set; }
    }

    private sealed class EntityWithId(int id, string name) : IEquatable<EntityWithId>
    {
        public int Id { get; } = id;
        public string Name { get; } = name;

        public bool Equals([NotNullWhen(true)] EntityWithId? other) => other is not null && Id == other.Id;

        public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as EntityWithId);

        public override int GetHashCode() => Id;
    }

    private sealed record PersonRecord(string Name, List<int> Scores);

    private sealed class ThrowingGetter(bool shouldThrow)
    {
        public int Value => shouldThrow ? throw new InvalidOperationException("The value is not available.") : 1;
    }

    private sealed class CaseCollidingMembersExpected
    {
        public string? Name { get; set; }

        [SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "The member name must differ only by case")]
        [SuppressMessage("Naming", "CA1708:Identifiers should differ by more than case", Justification = "The member name must differ only by case")]
        public string? name { get; set; }
    }

    private sealed class CaseCollidingMembersActual
    {
        public string? Name { get; set; }

        [SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "The member name must differ only by case")]
        [SuppressMessage("Naming", "CA1708:Identifiers should differ by more than case", Justification = "The member name must differ only by case")]
        public string? name { get; set; }
    }

    private sealed class ThrowingToStringKey(int value) : IEquatable<ThrowingToStringKey>
    {
        public int Value { get; } = value;

        public bool Equals([NotNullWhen(true)] ThrowingToStringKey? other) => other is not null && Value == other.Value;

        public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as ThrowingToStringKey);

        public override int GetHashCode() => Value;

        public override string ToString() => throw new InvalidOperationException("The key must not be formatted.");
    }

    private sealed class CountingItem(StrongBox<int> counter, int id, string name)
    {
        // Declared first, so comparing two items member by member always reads it.
        public object Payload
        {
            get
            {
                counter.Value++;
                return Id;
            }
        }

        public int Id { get; } = id;
        public string Name { get; } = name;
    }

    private sealed class SignatureItem
    {
        public int Number { get; set; }
        public double Ratio { get; set; }
        public int? Optional { get; set; }
    }
}
