using Xunit;

namespace Meziantou.Framework.Tests
{
    public class HashCodeCombinerTests
    {
        [Fact]
        public void HashCodeCombiner_ImplicitConversionInt()
        {
            var hashCodeCombiner = new HashCodeCombiner();
            hashCodeCombiner.Add(new object());
            int hashCode = hashCodeCombiner;

            Assert.Equal(hashCodeCombiner.HashCode, hashCode);
        }

        [Fact]
        public void HashCodeCombiner_Add()
        {
            var hashCodeCombiner = new HashCodeCombiner();
            hashCodeCombiner.Add(new object());
            Assert.NotEqual(0, hashCodeCombiner.HashCode);
        }

        [Fact]
        public void HashCodeCombiner_Add_NotEquals_AddEnumerable()
        {
            var hashCodeCombiner1 = new HashCodeCombiner();
            hashCodeCombiner1.Add(new object());

            var hashCodeCombiner2 = new HashCodeCombiner();
            hashCodeCombiner2.Add(new[] { new object() });

            var hashCodeCombiner3 = new HashCodeCombiner();
            hashCodeCombiner3.Add((object)new[] { new object() });

            Assert.NotEqual(hashCodeCombiner1.HashCode, hashCodeCombiner2.HashCode);
            Assert.NotEqual(hashCodeCombiner2.HashCode, hashCodeCombiner3.HashCode);
            Assert.NotEqual(hashCodeCombiner3.HashCode, hashCodeCombiner1.HashCode);
        }
    }
}
