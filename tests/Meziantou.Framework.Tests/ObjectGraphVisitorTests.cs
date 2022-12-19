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

        visitor.VisitedValues.Should().Contain(new object[] { 0, "abc", 1u });
    }

    [Fact]
    public void VisitRecursiveObject()
    {
        var visitor = new TestObjectGraphVisitor();
        var root = new Recursive("a", null);
        var child = new Recursive("b", root);

        visitor.Visit(child);

        visitor.VisitedValues.Should().Contain(new object[] { "a", "b" });
    }

    private sealed record Recursive(object Value, Recursive Parent);

    private sealed class TestObjectGraphVisitor : ObjectGraphVisitor
    {
        public List<PropertyInfo> VisitedProperties { get; } = new();
        public List<object> VisitedValues { get; } = new();

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
