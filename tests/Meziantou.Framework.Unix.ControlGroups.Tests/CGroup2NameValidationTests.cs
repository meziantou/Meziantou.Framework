namespace Meziantou.Framework.Unix.ControlGroups.Tests;

/// <summary>Name validation happens before any file system access, so these run on every OS without privileges.</summary>
public sealed class CGroup2NameValidationTests
{
    [Theory]
    [InlineData("/tmp/evil")]
    [InlineData("../../../tmp/evil")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateOrGetChild_ShouldRejectNamesThatAreNotASingleSegment(string name)
    {
        Assert.ThrowsAny<ArgumentException>(() => CGroup2.Root.CreateOrGetChild(name));
    }

    [Theory]
    [InlineData("/tmp/evil")]
    [InlineData("../../../tmp/evil")]
    [InlineData("a/b")]
    public void GetChild_ShouldRejectNamesThatAreNotASingleSegment(string name)
    {
        Assert.ThrowsAny<ArgumentException>(() => CGroup2.Root.GetChild(name));
    }

    [Theory]
    [InlineData("2MB/../../../etc/passwd")]
    [InlineData("../../2MB")]
    [InlineData("")]
    public void HugeTlb_ShouldRejectPageSizesThatAreNotASingleSegment(string pageSize)
    {
        Assert.ThrowsAny<ArgumentException>(() => CGroup2.Root.SetHugeTlbMax(pageSize, 1024));
        Assert.ThrowsAny<ArgumentException>(() => CGroup2.Root.GetHugeTlbMax(pageSize));
        Assert.ThrowsAny<ArgumentException>(() => CGroup2.Root.GetHugeTlbCurrent(pageSize));
        Assert.ThrowsAny<ArgumentException>(() => CGroup2.Root.GetHugeTlbEventsMax(pageSize));
    }

    [Theory]
    [InlineData("myapp")]
    [InlineData("worker.1")]
    [InlineData("test_cgroup_0")]
    public void ValidateSegment_ShouldAcceptOrdinaryNames(string name)
    {
        CGroup2.ValidateSegment(name, nameof(name));
    }
}
