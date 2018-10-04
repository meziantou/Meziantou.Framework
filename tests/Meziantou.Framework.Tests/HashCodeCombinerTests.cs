using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests
{
    [TestClass]
    public class HashCodeCombinerTests
    {
        [TestMethod]
        public void HashCodeCombiner_ImplicitConversionInt()
        {
            var hashCodeCombiner = new HashCodeCombiner();
            hashCodeCombiner.Add(new object());
            int hashCode = hashCodeCombiner;

            Assert.AreEqual(hashCodeCombiner.HashCode, hashCode);
        }

        [TestMethod]
        public void HashCodeCombiner_Add()
        {
            var hashCodeCombiner = new HashCodeCombiner();
            hashCodeCombiner.Add(new object());
            Assert.AreNotEqual(0, hashCodeCombiner.HashCode);
        }

        [TestMethod]
        public void HashCodeCombiner_Add_NotEquals_AddEnumerable()
        {
            var hashCodeCombiner1 = new HashCodeCombiner();
            hashCodeCombiner1.Add(new object());

            var hashCodeCombiner2 = new HashCodeCombiner();
            hashCodeCombiner2.Add(new[] { new object() });

            var hashCodeCombiner3 = new HashCodeCombiner();
            hashCodeCombiner3.Add((object)new[] { new object() });

            Assert.AreNotEqual(hashCodeCombiner1.HashCode, hashCodeCombiner2.HashCode);
            Assert.AreNotEqual(hashCodeCombiner2.HashCode, hashCodeCombiner3.HashCode);
            Assert.AreNotEqual(hashCodeCombiner3.HashCode, hashCodeCombiner1.HashCode);
        }
    }
}
