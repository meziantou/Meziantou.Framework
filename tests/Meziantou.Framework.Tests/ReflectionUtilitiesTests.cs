using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests;

public class ReflectionUtilitiesTests
{
    [Fact]
    public void IsFlagsEnum_ShouldDetectNonEnumeration()
    {
        Assert.False(ReflectionUtilities.IsFlagsEnum(typeof(ReflectionUtilitiesTests)));
    }

    [Fact]
    public void IsFlagsEnum_ShouldDetectNonFlagsEnumeration()
    {
        Assert.False(ReflectionUtilities.IsFlagsEnum(typeof(NonFlagsEnum)));
    }

    [Fact]
    public void IsFlagsEnum_ShouldDetectFlagsEnumeration()
    {
        Assert.True(ReflectionUtilities.IsFlagsEnum(typeof(FlagsEnum)));
    }

    [Theory]
    [InlineData(typeof(int), false)]
    [InlineData(typeof(int?), true)]
    [InlineData(typeof(MyNullable<int>), false)]
    public void IsNullableOf_ShouldDetectType(Type type, bool expectedResult)
    {
        Assert.Equal(expectedResult, ReflectionUtilities.IsNullableOfT(type));
    }

    private enum NonFlagsEnum
    {
        A,
        B,
    }

    [Flags]
    private enum FlagsEnum
    {
        A,
        B,
    }

    private static class MyNullable<T>
    {
    }
}
