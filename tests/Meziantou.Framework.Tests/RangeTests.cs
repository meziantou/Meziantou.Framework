using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests
{
    [TestClass]
    public class RangeTests
    {
        [DataTestMethod]
        [DataRow(0, 10, 5, true)]
        [DataRow(0, 10, 0, true)]
        [DataRow(0, 10, 10, true)]
        [DataRow(0, 10, 11, false)]
        [DataRow(0, 10, -1, false)]
        public void Range_IsInRangeInclusive_Value(int from, int to, int value, bool expectedValue)
        {
            var range = Range.Create(from, to);
            var result = range.IsInRangeInclusive(value);
            Assert.AreEqual(expectedValue, result);
        }

        [DataTestMethod]
        [DataRow(0, 10, 5, 8, true)]
        [DataRow(0, 10, 0, 10, true)]
        [DataRow(0, 10, 0, 5, true)]
        [DataRow(0, 10, 5, 10, true)]
        [DataRow(0, 10, 1, 11, false)]
        [DataRow(0, 10, -1, 4, false)]
        public void Range_IsInRangeInclusive_Range(int from1, int to1, int from2, int to2, bool expectedValue)
        {
            var range1 = Range.Create(from1, to1);
            var range2 = Range.Create(from2, to2);
            var result = range1.IsInRangeInclusive(range2);
            Assert.AreEqual(expectedValue, result);
        }

        [DataTestMethod]
        [DataRow(0, 10, 5, true)]
        [DataRow(0, 10, 0, false)]
        [DataRow(0, 10, 10, false)]
        [DataRow(0, 10, 11, false)]
        [DataRow(0, 10, -1, false)]
        public void Range_IsInRangeExclusive_Value(int from, int to, int value, bool expectedValue)
        {
            var range = Range.Create(from, to);
            var result = range.IsInRangeExclusive(value);
            Assert.AreEqual(expectedValue, result);
        }

        [DataTestMethod]
        [DataRow(0, 10, 5, 8, true)]
        [DataRow(0, 10, 0, 10, false)]
        [DataRow(0, 10, 0, 5, false)]
        [DataRow(0, 10, 5, 10, false)]
        [DataRow(0, 10, 1, 11, false)]
        [DataRow(0, 10, -1, 4, false)]
        public void Range_IsInRangeExclusive_Range(int from1, int to1, int from2, int to2, bool expectedValue)
        {
            var range1 = Range.Create(from1, to1);
            var range2 = Range.Create(from2, to2);
            var result = range1.IsInRangeExclusive(range2);
            Assert.AreEqual(expectedValue, result);
        }

        [DataTestMethod]
        [DataRow(0, 10, 5, true)]
        [DataRow(0, 10, 0, true)]
        [DataRow(0, 10, 10, false)]
        [DataRow(0, 10, 11, false)]
        [DataRow(0, 10, -1, false)]
        public void Range_IsInRangeLowerInclusive_Value(int from, int to, int value, bool expectedValue)
        {
            var range = Range.Create(from, to);
            var result = range.IsInRangeLowerInclusive(value);
            Assert.AreEqual(expectedValue, result);
        }

        [DataTestMethod]
        [DataRow(0, 10, 5, 8, true)]
        [DataRow(0, 10, 0, 10, false)]
        [DataRow(0, 10, 0, 5, true)]
        [DataRow(0, 10, 5, 10, false)]
        [DataRow(0, 10, 1, 11, false)]
        [DataRow(0, 10, -1, 4, false)]
        public void Range_IsInRangeLowerInclusive_Range(int from1, int to1, int from2, int to2, bool expectedValue)
        {
            var range1 = Range.Create(from1, to1);
            var range2 = Range.Create(from2, to2);
            var result = range1.IsInRangeLowerInclusive(range2);
            Assert.AreEqual(expectedValue, result);
        }

        [DataTestMethod]
        [DataRow(0, 10, 5, true)]
        [DataRow(0, 10, 0, false)]
        [DataRow(0, 10, 10, true)]
        [DataRow(0, 10, 11, false)]
        [DataRow(0, 10, -1, false)]
        public void Range_IsInRangeUpperInclusive_Value(int from, int to, int value, bool expectedValue)
        {
            var range = Range.Create(from, to);
            var result = range.IsInRangeUpperInclusive(value);
            Assert.AreEqual(expectedValue, result);
        }

        [DataTestMethod]
        [DataRow(0, 10, 5, 8, true)]
        [DataRow(0, 10, 0, 10, false)]
        [DataRow(0, 10, 0, 5, false)]
        [DataRow(0, 10, 5, 10, true)]
        [DataRow(0, 10, 1, 11, false)]
        [DataRow(0, 10, -1, 4, false)]
        public void Range_IsInRangeUpperInclusive_Range(int from1, int to1, int from2, int to2, bool expectedValue)
        {
            var range1 = Range.Create(from1, to1);
            var range2 = Range.Create(from2, to2);
            var result = range1.IsInRangeUpperInclusive(range2);
            Assert.AreEqual(expectedValue, result);
        }
    }
}
