using System.Linq.Expressions;

namespace Meziantou.Framework.Tests;

public class ExpressionExtensions
{
    [Fact]
    public void Documentation_Combine()
    {
        #region ExpressionExtensions_Combine
        Expression<Func<int, bool>> isPositive = n => n > 0;
        Expression<Func<int, bool>> isSmall = n => n < 10;

        // The parameters are rebound: the result is one lambda expression with one parameter
        var isPositiveAndSmall = isPositive.AndAlso(isSmall);
        var isNotPositiveOrNotSmall = isPositiveAndSmall.Negate();
        var isNegativeOrSmall = isPositive.Negate().OrElse(isSmall);

        int[] numbers = [-5, 3, 42];
        Assert.Equal([3], numbers.AsQueryable().Where(isPositiveAndSmall));
        Assert.Equal([-5, 42], numbers.AsQueryable().Where(isNotPositiveOrNotSmall));
        Assert.Equal([-5, 3], numbers.AsQueryable().Where(isNegativeOrSmall));
        #endregion
    }

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
