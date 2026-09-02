using System.Reflection;

namespace Meziantou.Framework.Tests;

public sealed class ObjectGraphVisitorTests
{
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
}
