using Xunit;

namespace Meziantou.Framework.InlineSnapshotTesting.Tests;
public sealed class SnapshotComparerTests
{
    [Fact]
    public void NormalizeLineEndingAndTabs()
    {
        var actual = SnapshotComparer.Default.NormalizeValue("ab\r\n  \n\tcd");
        Assert.Equal("ab\n\n    cd", actual);
    }
}
