using System;
using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public sealed class DataBinderTests
    {
        [Fact]
        public void BasicExpression()
        {
            var obj = new { A = "test" };
            var actual = DataBinder.Eval(obj, "A");
            actual.Should().Be("test");
        }

        [Fact]
        public void NestedExpression()
        {
            var obj = new { A = new { B = new[] { "a", "b", "c" } } };
            var actual = DataBinder.Eval(obj, "A.B[1]");
            actual.Should().Be("b");
        }

        [Fact]
        public void MissingProperty()
        {
            var obj = new { A = "test" };
            new Func<object>(() => DataBinder.Eval(obj, "B")).Should().ThrowExactly<ArgumentException>();
        }

        [Fact]
        public void NonIndexableObjectg()
        {
            var obj = new { A = 0 };
            new Func<object>(() => DataBinder.Eval(obj, "A[0]")).Should().ThrowExactly<ArgumentException>();
        }
    }
}
