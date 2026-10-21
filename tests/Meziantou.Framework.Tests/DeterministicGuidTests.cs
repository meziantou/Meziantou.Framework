namespace Meziantou.Framework.Tests;

public sealed class DeterministicGuidTests
{
    [Fact]
    public void DeterministicGuid_Create_Example()
    {
        #region DeterministicGuid_Create
        // Generate a GUID from a DNS name
        var guid = DeterministicGuid.Create(DeterministicGuid.DnsNamespace, "example.com", DeterministicGuidVersion.Version5);
        Assert.Equal(Guid.Parse("cfbff0d1-9375-5685-968c-48ce8b15ae17"), guid);

        // The same input will always generate the same GUID
        var guid2 = DeterministicGuid.Create(DeterministicGuid.DnsNamespace, "example.com", DeterministicGuidVersion.Version5);
        Assert.Equal(guid, guid2);
        #endregion
    }

    [Fact]
    public void DeterministicGuid_Version5()
    {
        var actual = DeterministicGuid.Create(DeterministicGuid.DnsNamespace, "www.example.com", DeterministicGuidVersion.Version5);
        Assert.Equal(Guid.Parse("2ed6657d-e927-568b-95e1-2665a8aea6a2"), actual);
    }

    [Theory]
    [InlineData("www.example.com", "5df41881-3aed-3515-88a7-2f4a814cf09e")]
    [InlineData("www.widgets.com", "3d813cbb-47fb-32ba-91df-831e1593ac29")]
    public void DeterministicGuid_Version3_Dns(string name, string expectedGuid)
    {
        var actual = DeterministicGuid.Create(DeterministicGuid.DnsNamespace, name, DeterministicGuidVersion.Version3);
        Assert.Equal(Guid.Parse(expectedGuid), actual);
    }
}
