using System.Reflection;

namespace Meziantou.Framework.Tests;

public sealed class ObjectGraphVisitorTests
{
    [Fact]
    public void Documentation_Visit()
    {
        #region ObjectGraphVisitor_Visit
        var order = new
        {
            Customer = new { Name = "Ada" },
            Lines = new[] { "Keyboard", "Mouse" },
        };

        var collector = new StringCollector();
        collector.Visit(order);

        Assert.Equal(["Ada", "Keyboard", "Mouse"], collector.Strings.Order(StringComparer.Ordinal));
        #endregion
    }

    #region ObjectGraphVisitor_StringCollector
    private sealed class StringCollector : ObjectGraphVisitor
    {
        public List<string> Strings { get; } = [];

        // Called once for every object of the graph: the root, property values and collection items
        protected override void VisitValue(object value)
        {
            if (value is string text)
            {
                Strings.Add(text);
            }
        }
    }
    #endregion

    [Fact]
    public void VisitAnonymousObject()
    {
        var visitor = new TestObjectGraphVisitor();
        visitor.Visit(new { A = 0, B = new object[] { "abc", 1u } });

        Assert.Contains(0, visitor.VisitedValues);
        Assert.Contains("abc", visitor.VisitedValues);
        Assert.Contains(1u, visitor.VisitedValues);
        Assert.Contains("Length", visitor.VisitedProperties.Select(p => p.Name));
    }

    [Fact]
    public void VisitRecursiveObject()
    {
        var visitor = new TestObjectGraphVisitor();
        var root = new Recursive("a", null);
        var child = new Recursive("b", root);

        visitor.Visit(child);

        Assert.Contains("a", visitor.VisitedValues);
        Assert.Contains("b", visitor.VisitedValues);
    }

    [Fact]
    public void VisitIndexer()
    {
        var visitor = new TestObjectGraphVisitor();
        visitor.Visit(new Indexer());
        Assert.Empty(visitor.VisitedProperties);
    }

    private sealed record Recursive(object Value, Recursive? Parent);

    private sealed class Indexer
    {
        public int this[int index] => index;
    }

    private sealed class TestObjectGraphVisitor : ObjectGraphVisitor
    {
        public List<PropertyInfo> VisitedProperties { get; } = [];
        public List<object> VisitedValues { get; } = [];

        protected override void VisitProperty(object parentInstance, PropertyInfo property, object? value)
        {
            VisitedProperties.Add(property);
        }

        protected override void VisitValue(object value)
        {
            VisitedValues.Add(value);
        }
    }

    [Fact]
    public void VisitsDistinctInstancesThatAreEqualByValue()
    {
        var first = new EqualByName { Name = "same" };
        var second = new EqualByName { Name = "same" };
        var third = new EqualByName { Name = "other" };

        Assert.NotSame(first, second);
        Assert.Equal(first, second);

        var visitor = new InstanceCollector();
        visitor.Visit(new List<EqualByName> { first, second, third });

        Assert.HasCount(3, visitor.Instances);
        Assert.Contains(first, visitor.Instances);
        Assert.Contains(second, visitor.Instances);
        Assert.Contains(third, visitor.Instances);
    }

    [Fact]
    public void StillBreaksReferenceCycles()
    {
        var node = new Node();
        node.Self = node;

        var visitor = new InstanceCollector();
        visitor.Visit(node);

        Assert.Single(visitor.Instances, i => ReferenceEquals(i, node));
    }

    private sealed class InstanceCollector : ObjectGraphVisitor
    {
        public HashSet<object> Instances { get; } = new(ReferenceEqualityComparer.Instance);

        protected override void VisitValue(object value)
        {
            if (value is EqualByName or Node)
            {
                Instances.Add(value);
            }
        }
    }

    private sealed class EqualByName
    {
        public string? Name { get; set; }

        public override bool Equals([NotNullWhen(true)] object? obj) => obj is EqualByName other && string.Equals(other.Name, Name, StringComparison.Ordinal);
        public override int GetHashCode() => Name?.GetHashCode(StringComparison.Ordinal) ?? 0;
    }

    private sealed class Node
    {
        public Node? Self { get; set; }
    }

    [Fact]
    public void VisitDateTimeProperties()
    {
        var visitor = new TestObjectGraphVisitor();
        var when = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        visitor.Visit(new DateHolder { When = when, Offset = new DateTimeOffset(when), Duration = TimeSpan.FromMinutes(1), Id = Guid.NewGuid() });

        Assert.Contains(when, visitor.VisitedValues);
        Assert.Equal(["Duration", "Id", "Offset", "When"], visitor.VisitedProperties.Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void VisitValueTypeReturningItself()
    {
        var visitor = new TestObjectGraphVisitor();
        visitor.Visit(new SelfReturning(1));

        Assert.HasCount(2, visitor.VisitedValues);
        Assert.Contains(new SelfReturning(1), visitor.VisitedValues);
        Assert.Contains(1, visitor.VisitedValues);
    }

    [Fact]
    public void VisitEqualValueTypesInDifferentBranches()
    {
        var visitor = new TestObjectGraphVisitor();
        visitor.Visit(new { A = 1, B = 1 });

        Assert.Equal(2, visitor.VisitedValues.Count(value => value is 1));
    }

    private sealed class DateHolder
    {
        public DateTime When { get; set; }
        public DateTimeOffset Offset { get; set; }
        public TimeSpan Duration { get; set; }
        public Guid Id { get; set; }
    }

    private readonly record struct SelfReturning(int Value)
    {
        public SelfReturning Self => this;
    }
}
