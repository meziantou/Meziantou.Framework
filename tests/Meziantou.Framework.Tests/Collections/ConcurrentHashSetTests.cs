using FluentAssertions;
using Meziantou.Framework.Collections;
using Xunit;

namespace Meziantou.Framework.Tests.Collections
{
    public class ConcurrentHashSetTests
    {
        [Fact]
        public void TestConcurrentHashSet()
        {
            ConcurrentHashSet<int> set = new();
            set.Add(1).Should().BeTrue();
            set.Add(2).Should().BeTrue();
            set.Add(3).Should().BeTrue();
            set.Add(3).Should().BeFalse();

            set.Should().Contain(1);
            set.Should().NotContain(4);

            set.Should().HaveCount(3);
            set.Should().BeEquivalentTo(new[] { 1, 2, 3 });

            set.Clear();
            set.Should().BeEmpty();

            set.AddRange(4, 5, 6);
            set.Should().BeEquivalentTo(new[] { 4, 5, 6 });
        }
    }
}
