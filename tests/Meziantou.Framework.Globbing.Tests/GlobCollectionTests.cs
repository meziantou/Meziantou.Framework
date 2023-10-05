using Xunit;

namespace Meziantou.Framework.Globbing.Tests;
public sealed class GlobCollectionTests
{
    [Fact]
    public void CanUseCollectionInitializer()
    {
        var a = Glob.Parse("a", GlobOptions.None);
        var b = Glob.Parse("b", GlobOptions.None);

        GlobCollection globs = [a, b];
        Assert.Collection(globs,
            item => Assert.Equal(a, item),
            item => Assert.Equal(b, item));
    }
}
