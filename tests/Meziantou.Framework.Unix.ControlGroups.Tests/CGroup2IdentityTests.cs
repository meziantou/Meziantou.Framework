namespace Meziantou.Framework.Unix.ControlGroups.Tests;

/// <summary>Identity and equality do not touch the file system, so these run on every OS without privileges.</summary>
public sealed class CGroup2IdentityTests
{
    [Fact]
    public void Root_ShouldBeNamedAsASegmentNotAPath()
    {
        Assert.Equal("/", CGroup2.Root.Name);
    }

    [Fact]
    public void Root_ShouldStillResolveToTheMountPoint()
    {
        Assert.Equal("/sys/fs/cgroup", CGroup2.Root.ToString());
    }

    [Fact]
    public void Root_ShouldHaveNoParent()
    {
        Assert.Null(CGroup2.Root.Parent);
    }

    [Fact]
    public void Root_ShouldReturnTheSameInstanceOnEveryAccess()
    {
        Assert.Same(CGroup2.Root, CGroup2.Root);
    }

    [Fact]
    public void Equals_ShouldCompareByPath()
    {
        var root = CGroup2.Root;
        Assert.Equal(root, CGroup2.Root);
        Assert.Equal(root.GetHashCode(), CGroup2.Root.GetHashCode());
    }

    [Fact]
    public void Equals_ShouldReturnFalseForNull()
    {
        Assert.False(CGroup2.Root.Equals(null));
        Assert.False(CGroup2.Root.Equals(obj: null));
    }

    [Fact]
    public void EqualityOperators_ShouldCompareByPath()
    {
        var root = CGroup2.Root;
        CGroup2? nothing = null;

        var sameIsEqual = root == CGroup2.Root;
        var sameIsNotEqual = root != CGroup2.Root;
        var valueEqualsNull = root == nothing;
        var valueDiffersFromNull = root != nothing;
        var nullEqualsNull = nothing == null;
        var nullDiffersFromNull = nothing != null;

        Assert.True(sameIsEqual);
        Assert.False(sameIsNotEqual);
        Assert.False(valueEqualsNull);
        Assert.True(valueDiffersFromNull);
        Assert.True(nullEqualsNull);
        Assert.False(nullDiffersFromNull);
    }
}
