using FluentAssertions;
using Meziantou.Framework.Collections;
using Xunit;

namespace Meziantou.Framework.Tests.Collections;

public sealed class AppendOnlyCollectionTests
{
    [Fact]
    public void Test()
    {
        var collection = new AppendOnlyCollection<int>();
        collection.Should().BeEmpty();

        collection.Add(0);
        collection.Should().BeEquivalentTo(new[] { 0 });

        collection.Add(1);
        collection.Should().BeEquivalentTo(new[] { 0, 1 });

        for (var i = 2; i < 10_000; i++)
        {
            collection.Add(i);
        }

        collection.Should().HaveCount(10_000);

        collection[0].Should().Be(0);
        collection[1000].Should().Be(1000);
    }
}
