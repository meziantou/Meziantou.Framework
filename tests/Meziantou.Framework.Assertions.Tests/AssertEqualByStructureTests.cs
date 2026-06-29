using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertEqualByStructureTests
{
    [Fact]
    public void Null_Success()
    {
        AssertionsAssert.EqualByStructure(expected: null, actual: null);
    }

    [Fact]
    public void Scalar_Success()
    {
        AssertionsAssert.EqualByStructure(42, 42L);
        AssertionsAssert.EqualByStructure("value", "value");
    }

    [Fact]
    public void DifferentTypesWithSamePublicProperties_Success()
    {
        var expected = new ExpectedPerson { Name = "Alice", Age = 42 };
        var actual = new ActualPerson { Name = "Alice", Age = 42 };

        AssertionsAssert.EqualByStructure(expected, actual);
    }

    [Fact]
    public void PublicFieldsAndProperties_Success()
    {
        var expected = new FieldPerson { Name = "Alice", Age = 42 };
        var actual = new ActualPerson { Name = "Alice", Age = 42 };

        AssertionsAssert.EqualByStructure(expected, actual);
    }

    [Fact]
    public void PrivateMembersAreIgnored_Success()
    {
        var expected = new PersonWithPrivateState("Alice", "expected secret");
        var actual = new PersonWithPrivateState("Alice", "actual secret");

        AssertionsAssert.EqualByStructure(expected, actual);
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

        AssertionsAssert.EqualByStructure(expected, actual);
    }

    [Fact]
    public void Collections_Success()
    {
        var expected = new PersonWithScores { Name = "Alice", Scores = new[] { 1, 2, 3 } };
        var actual = new PersonWithScores { Name = "Alice", Scores = new[] { 1L, 2L, 3L } };

        AssertionsAssert.EqualByStructure(expected, actual);
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

        AssertionsAssert.EqualByStructure(expected, actual);
    }

    [Fact]
    public void FailsWhenPropertyValueDiffers()
    {
        var expected = new ExpectedPerson { Name = "Alice", Age = 42 };
        var actual = new ActualPerson { Name = "Bob", Age = 42 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualByStructure(expected, actual), """
            Assert.EqualByStructure() assertion failed.
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

        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualByStructure(expected, actual, "custom message"), """
            Assert.EqualByStructure() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $.Address.ZipCode
            Reason: Values differ.
            Expected: 75000
            Actual:   69000
            Message: custom message
            """);
    }

    [Fact]
    public void FailsWhenActualMemberIsMissing()
    {
        var expected = new ExpectedPerson { Name = "Alice", Age = 42 };
        var actual = new PersonWithNameOnly { Name = "Alice" };

        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualByStructure(expected, actual), """
            Assert.EqualByStructure() assertion failed.
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

        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualByStructure(expected, actual), """
            Assert.EqualByStructure() assertion failed.
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

        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualByStructure(expected, actual), """
            Assert.EqualByStructure() assertion failed.
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

        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualByStructure(expected, actual), """
            Assert.EqualByStructure() assertion failed.
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

        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualByStructure(expected, actual), """
            Assert.EqualByStructure() assertion failed.
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

        AssertionTestHelpers.Validate(() => AssertionsAssert.EqualByStructure(expected, actual), """
            Assert.EqualByStructure() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Path: $.Name
            Reason: Values differ.
            Expected: <null>
            Actual:   "Alice"
            """);
    }

    [Fact]
    public void NotEqualByStructure_Success()
    {
        var expected = new ExpectedPerson { Name = "Alice", Age = 42 };
        var actual = new ActualPerson { Name = "Alice", Age = 43 };

        AssertionsAssert.NotEqualByStructure(expected, actual);
    }

    [Fact]
    public void NotEqualByStructure_Fails()
    {
        var expected = new ExpectedPerson { Name = "Alice", Age = 42 };
        var actual = new ActualPerson { Name = "Alice", Age = 42 };

        AssertionTestHelpers.Validate(() => AssertionsAssert.NotEqualByStructure(expected, actual), """
            Assert.NotEqualByStructure() assertion failed.
            Expected expression: expected
            Actual expression:   actual
            Not expected: Meziantou.Framework.Assertions.Tests.AssertEqualByStructureTests+ExpectedPerson
            Actual:              Meziantou.Framework.Assertions.Tests.AssertEqualByStructureTests+ActualPerson
            """);
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

    private sealed class Node
    {
        public string? Name { get; set; }
        public Node? Next { get; set; }
    }
}
