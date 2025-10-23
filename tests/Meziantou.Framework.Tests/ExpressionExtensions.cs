using System.Linq.Expressions;

namespace Meziantou.Framework.Tests;

public class ExpressionExtensions
{
    [Fact]
    public void AndAlso()
    {
        Expression<Func<int, bool>> func1 = n => n > 0;
        Expression<Func<int, bool>> func2 = n => n < 10;

        var func = func1.AndAlso(func2).Compile();
        Assert.True(func(1));
        Assert.False(func(100));
        Assert.False(func(0));
    }

    [Fact]
    public void OrElse()
    {
        Expression<Func<int, bool>> func1 = n => n < 0;
        Expression<Func<int, bool>> func2 = n => n > 10;

        var func = func1.OrElse(func2).Compile();
        Assert.False(func(1));
        Assert.True(func(100));
        Assert.True(func(-1));
    }
}
