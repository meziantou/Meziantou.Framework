#pragma warning disable CS0618 // Type or member is obsolete
using FluentAssertions;
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

            hashCode.Should().Be(hashCodeCombiner.HashCode);
        }

        [Fact]
        public void HashCodeCombiner_Add()
        {
            var hashCodeCombiner = new HashCodeCombiner();
            hashCodeCombiner.Add(new object());
            hashCodeCombiner.HashCode.Should().NotBe(0);
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

            hashCodeCombiner2.HashCode.Should().NotBe(hashCodeCombiner1.HashCode);
            hashCodeCombiner3.HashCode.Should().NotBe(hashCodeCombiner2.HashCode);
            hashCodeCombiner1.HashCode.Should().NotBe(hashCodeCombiner3.HashCode);
        }
    }
}
