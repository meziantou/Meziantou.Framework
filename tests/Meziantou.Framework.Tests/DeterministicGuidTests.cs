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

    [Theory]
    [InlineData(DeterministicGuidVersion.Version3)]
    [InlineData(DeterministicGuidVersion.Version5)]
    public void StringAndBytesOverloadsMatch(DeterministicGuidVersion version)
    {
        foreach (var length in new[] { 0, 1, 239, 240, 241, 1000 })
        {
            var name = new string('\u00e9', length);
            var expected = DeterministicGuid.Create(DeterministicGuid.UrlNamespace, Encoding.UTF8.GetBytes(name), version);
            Assert.Equal(expected, DeterministicGuid.Create(DeterministicGuid.UrlNamespace, name, version));
        }
    }

    [Fact]
    public void LongName()
    {
        var name = new string('a', 10_000);
        var expected = ReferenceImplementation(DeterministicGuid.DnsNamespace, Encoding.UTF8.GetBytes(name));
        Assert.Equal(expected, DeterministicGuid.Create(DeterministicGuid.DnsNamespace, name, DeterministicGuidVersion.Version5));
    }

    [Fact]
    public void InvalidVersion()
    {
        Assert.Throws<ArgumentOutOfRangeException>("version", () => DeterministicGuid.Create(DeterministicGuid.DnsNamespace, "a", (DeterministicGuidVersion)4));
    }

    // Straightforward SHA-1 (version 5) implementation of RFC 4122, section 4.3
    private static Guid ReferenceImplementation(Guid @namespace, byte[] name)
    {
        var namespaceBytes = @namespace.ToByteArray(bigEndian: true);
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
        var hash = System.Security.Cryptography.SHA1.HashData([.. namespaceBytes, .. name]);
#pragma warning restore CA5350
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
}
