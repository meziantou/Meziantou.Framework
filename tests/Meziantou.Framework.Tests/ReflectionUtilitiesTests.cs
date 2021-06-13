using System;
using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public class ReflectionUtilitiesTests
    {
        [Fact]
        public void IsFlagsEnum_ShouldDetectNonEnumeration()
        {
            ReflectionUtilities.IsFlagsEnum(typeof(ReflectionUtilitiesTests)).Should().BeFalse();
        }

        [Fact]
        public void IsFlagsEnum_ShouldDetectNonFlagsEnumeration()
        {
            ReflectionUtilities.IsFlagsEnum(typeof(NonFlagsEnum)).Should().BeFalse();
        }

        [Fact]
        public void IsFlagsEnum_ShouldDetectFlagsEnumeration()
        {
            ReflectionUtilities.IsFlagsEnum(typeof(FlagsEnum)).Should().BeTrue();
        }

        [Theory]
        [InlineData(typeof(int), false)]
        [InlineData(typeof(int?), true)]
        [InlineData(typeof(MyNullable<int>), false)]
        public void IsNullableOf_ShouldDetectType(Type type, bool expectedResult)
        {
            ReflectionUtilities.IsNullableOfT(type).Should().Be(expectedResult);
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
}
