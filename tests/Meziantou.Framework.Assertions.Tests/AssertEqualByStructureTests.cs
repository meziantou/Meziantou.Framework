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
            Not expected: [[a, 1], [b, 2]]
            Actual:       [[b, 2], [a, 1]]
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
}
