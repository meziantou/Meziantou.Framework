using FluentAssertions;
using Meziantou.Framework.Collections;
using Xunit;

namespace Meziantou.Framework.Tests.Collections
{
    public sealed class SortedListTests
    {
        [Fact]
        public void Test()
        {
            var list = new SortedList<int> { 1, 3, 2 };
            list.Should().Equal(new[] { 1, 2, 3 });

            list.IndexOf(1).Should().Be(0);
            list.IndexOf(2).Should().Be(1);
            list.IndexOf(42).Should().Be(-1);

            list.Remove(2);
            list.Should().Equal(new[] { 1, 3 });

            list.Remove(1);
            list.Should().Equal(new[] { 3 });

            list.Remove(3);
            list.Should().BeEmpty();
        }

        [Fact]
        public void AsSpan()
        {
            var list = new SortedList<int> { 1, 3, 2 };

            list.UnsafeAsReadOnlySpan().Length.Should().Be(3);
        }
    }
}
