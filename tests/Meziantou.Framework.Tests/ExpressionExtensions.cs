using System;
using System.Linq.Expressions;
using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public class ExpressionExtensions
    {
        [Fact]
        public void AndAlso()
        {
            Expression<Func<int, bool>> func1 = n => n > 0;
            Expression<Func<int, bool>> func2 = n => n < 10;

            var func = func1.AndAlso(func2).Compile();
            func(1).Should().BeTrue();
            func(100).Should().BeFalse();
            func(0).Should().BeFalse();
        }

        [Fact]
        public void OrElse()
        {
            Expression<Func<int, bool>> func1 = n => n < 0;
            Expression<Func<int, bool>> func2 = n => n > 10;

            var func = func1.OrElse(func2).Compile();
            func(1).Should().BeFalse();
            func(100).Should().BeTrue();
            func(-1).Should().BeTrue();
        }
    }
}
