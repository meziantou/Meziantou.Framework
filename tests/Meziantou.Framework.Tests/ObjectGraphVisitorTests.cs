using System.Reflection;
using Xunit;

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
