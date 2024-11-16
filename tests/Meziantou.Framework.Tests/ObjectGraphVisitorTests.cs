using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests;

public sealed class ObjectGraphVisitorTests
{
    [Fact]
    public void VisitAnonymousObject()
    {
        var visitor = new TestObjectGraphVisitor();
        visitor.Visit(new { A = 0, B = new object[] { "abc", 1u } });

        visitor.VisitedValues.Should().Contain([0, "abc", 1u]);
        visitor.VisitedProperties.Select(p => p.Name).Should().Contain(["Length"]);
    }

    [Fact]
    public void VisitRecursiveObject()
    {
        var visitor = new TestObjectGraphVisitor();
        var root = new Recursive("a", null);
        var child = new Recursive("b", root);

        visitor.Visit(child);

        visitor.VisitedValues.Should().Contain(["a", "b"]);
    }

    [Fact]
    public void VisitIndexer()
    {
        var visitor = new TestObjectGraphVisitor();
        visitor.Visit(new Indexer());

        visitor.VisitedProperties.Should().BeEmpty();
    }

    private sealed record Recursive(object Value, Recursive Parent);

    private sealed class Indexer
    {
        public int this[int index] => index;
    }

    private sealed class TestObjectGraphVisitor : ObjectGraphVisitor
    {
        public List<PropertyInfo> VisitedProperties { get; } = [];
        public List<object> VisitedValues { get; } = [];

        protected override void VisitProperty(object parentInstance, PropertyInfo property, object value)
        {
            VisitedProperties.Add(property);
        }

        protected override void VisitValue(object value)
        {
            VisitedValues.Add(value);
        }
    }
}
