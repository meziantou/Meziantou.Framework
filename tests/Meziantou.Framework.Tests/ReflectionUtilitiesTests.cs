using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests
{
    [TestClass]
    public class ReflectionUtilitiesTests
    {
        [TestMethod]
        public void IsFlagsEnum_ShouldDetectNonEnumeration()
        {
            Assert.IsFalse(ReflectionUtilities.IsFlagsEnum(typeof(ReflectionUtilitiesTests)));
        }

        [TestMethod]
        public void IsFlagsEnum_ShouldDetectNonFlagsEnumeration()
        {
            Assert.IsFalse(ReflectionUtilities.IsFlagsEnum(typeof(NonFlagsEnum)));
        }

        [TestMethod]
        public void IsFlagsEnum_ShouldDetectFlagsEnumeration()
        {
            Assert.IsTrue(ReflectionUtilities.IsFlagsEnum(typeof(FlagsEnum)));
        }

        [DataTestMethod]
        [DataRow(typeof(int), false)]
        [DataRow(typeof(int?), true)]
        [DataRow(typeof(MyNullable<int>), false)]
        public void IsNullableOf_ShouldDetectType(Type type, bool expectedResult)
        {
            Assert.AreEqual(expectedResult, ReflectionUtilities.IsNullableOfT(type));
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
