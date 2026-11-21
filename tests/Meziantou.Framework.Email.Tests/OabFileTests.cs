namespace Meziantou.Framework.Email.Tests;

/// <summary>Tests of the offline address books: the example of [MS-OXOAB] 3.3, read and written byte for byte, and round trips.</summary>
public sealed class OabFileTests
{
    internal static string GetPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", "Oab", fileName);

    [Fact]
    public void SpecificationExample_Read()
    {
        var file = OabFile.Load(File.ReadAllBytes(GetPath("ms-oxoab-example.oab")));
        Assert.Equal(0x7FC0DAF7u, file.Checksum);
        Assert.Equal("\\Global Address List", file.Name);
        Assert.Equal(6, file.Sequence);
        Assert.Equal("d4f244a8-a8ec-442a-87a3-5236f82cabdc", file.Header.GetString(0x6802));
        Assert.Equal([MapiPropertyTag.FromValue(0x3003001E), MapiPropertyTag.FromValue(0x39FE001F), MapiPropertyTag.FromValue(0x3001001F), MapiPropertyTag.FromValue(0x0FFE0003), MapiPropertyTag.FromValue(0x39000003), MapiPropertyTag.FromValue(0x68051003)], file.RecordProperties.Select(property => property.Tag));
        Assert.Equal([OabPropertyFlags.PrimaryKey, OabPropertyFlags.PrimaryKey, OabPropertyFlags.AnrIndex, OabPropertyFlags.None, OabPropertyFlags.None, OabPropertyFlags.None], file.RecordProperties.Select(property => property.Flags));
        Assert.Equal(["Lisa Miller", "Administrator"], file.Records.Select(record => record.DisplayName));
        Assert.Equal("LisaM@example.com", file.Records[0].SmtpAddress);
        Assert.Equal("/o=example/ou=Exchange Administrative Group (FYDIBOHF23SPDLT)/cn=Recipients/cn=Lisa Miller", file.Records[0].EmailAddress);
        Assert.Equal(6, file.Records[0].ObjectType);
        Assert.Equal(0, file.Records[0].DisplayType);
    }

    [Fact]
    public void SpecificationExample_Write()
    {
        // The writer writes the example of the specification byte for byte, with its checksum
        var bytes = File.ReadAllBytes(GetPath("ms-oxoab-example.oab"));
        Assert.Equal(bytes, OabFile.Load(bytes).ToByteArray());
    }

    [Fact]
    public void Usage_Snippet()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".oab");
        try
        {
            #region OabFile_Usage
            var book = OabFile.Create();
            book.Header.Values[MapiPropertyTag.FromValue(0x6800001F)] = "\\Global Address List";
            book.Header.Values[MapiPropertyTag.FromValue(0x68010003)] = 1;
            var record = new OabRecord();
            record.Values[MapiPropertyTag.FromValue(0x3003001E)] = "/o=Example/ou=Exchange Administrative Group/cn=Recipients/cn=alice";
            record.Values[MapiPropertyTag.FromValue(0x39FE001F)] = "alice@example.com";
            record.Values[MapiPropertyTag.FromValue(0x3001001F)] = "Alice Martin";
            record.Values[MapiPropertyTag.FromValue(0x800F101F)] = new[] { "SMTP:alice@example.com", "smtp:a.martin@example.com" };
            record.Values[MapiPropertyTag.FromValue(0x0FFE0003)] = 6;
            book.Records.Add(record);
            book.Save(path);

            var file = OabFile.Load(path);
            Assert.Equal("\\Global Address List", file.Name);
            var alice = file.Records.Single();
            Assert.Equal("alice@example.com", alice.SmtpAddress);
            Assert.Equal(["SMTP:alice@example.com", "smtp:a.martin@example.com"], alice.ProxyAddresses);

            // A record as a vCard, or as a contact of Outlook
            var card = VCard.FromOab(alice);
            Assert.Equal("Alice Martin", card.FormattedName);
            #endregion

            Assert.Equal("alice@example.com", card.ToMapiContactBuilder().Email1Address);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AllTypes_RoundTrip()
    {
        var book = OabFile.Create();
        book.RecordProperties.Add(new OabPropertyDefinition(MapiPropertyTag.FromValue(0x68051003)));
        book.RecordProperties.Add(new OabPropertyDefinition(MapiPropertyTag.FromValue(0x8008101E)));
        book.RecordProperties.Add(new OabPropertyDefinition(MapiPropertyTag.FromValue(0x8CD8000D)));
        var record = new OabRecord();
        record.Values[MapiPropertyTag.FromValue(0x3001001F)] = "Zoë 😀";
        record.Values[MapiPropertyTag.FromValue(0x0FFE0003)] = -1;
        record.Values[MapiPropertyTag.FromValue(0x39000003)] = 300;
        record.Values[MapiPropertyTag.FromValue(0x3A40000B)] = true;
        record.Values[MapiPropertyTag.FromValue(0x3A220102)] = new byte[] { 0, 1, 2, 255 };
        record.Values[MapiPropertyTag.FromValue(0x3A701102)] = new[] { new byte[] { 1 }, new byte[200] };
        record.Values[MapiPropertyTag.FromValue(0x68051003)] = new[] { unchecked((int)0x8C9E0102), 127, 128, 65536 };
        record.Values[MapiPropertyTag.FromValue(0x8008101E)] = new[] { "/o=Example/cn=Group1", "/o=Example/cn=Group2" };
        record.Values[MapiPropertyTag.FromValue(0x3A08001F)] = "";
        book.Records.Add(record);
        book.Records.Add(new OabRecord());

        var copy = OabFile.Load(book.ToByteArray());
        Assert.HasCount(2, copy.Records);
        var values = copy.Records[0].Values;
        Assert.Equal("Zoë 😀", values[MapiPropertyTag.FromValue(0x3001001F)]);
        Assert.Equal(-1, values[MapiPropertyTag.FromValue(0x0FFE0003)]);
        Assert.Equal(300, values[MapiPropertyTag.FromValue(0x39000003)]);
        Assert.Equal(true, values[MapiPropertyTag.FromValue(0x3A40000B)]);
        Assert.Equal(new byte[] { 0, 1, 2, 255 }, values[MapiPropertyTag.FromValue(0x3A220102)]);
        Assert.Equal(new[] { unchecked((int)0x8C9E0102), 127, 128, 65536 }, values[MapiPropertyTag.FromValue(0x68051003)]);
        Assert.Equal(new[] { "/o=Example/cn=Group1", "/o=Example/cn=Group2" }, values[MapiPropertyTag.FromValue(0x8008101E)]);
        Assert.HasCount(2, (byte[][])values[MapiPropertyTag.FromValue(0x3A701102)]);

        // An empty string is not written, as the specification requires
        Assert.False(values.ContainsKey(MapiPropertyTag.FromValue(0x3A08001F)));
        Assert.Empty(copy.Records[1].Values);
    }

    [Fact]
    public void Invalid_Throws()
    {
        var bytes = File.ReadAllBytes(GetPath("ms-oxoab-example.oab"));
        Assert.Throws<InvalidEmailDataException>(() => OabFile.Load(bytes.AsSpan(0, 8)));
        Assert.Throws<InvalidEmailDataException>(() => OabFile.Load([3, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));

        var modified = (byte[])bytes.Clone();
        modified[320] ^= 0x01; // A character of a distinguished name
        Assert.Throws<InvalidEmailDataException>(() => OabFile.Load(modified));
        _ = OabFile.Load(modified, verifyChecksum: false);
        Assert.Throws<InvalidEmailDataException>(() => OabFile.Load(bytes.AsSpan(0, bytes.Length - 10), verifyChecksum: false));

        var book = OabFile.Create();
        book.Records.Add(new OabRecord { Values = { [MapiPropertyTag.FromValue(0x3001001F)] = 42 } });
        Assert.Throws<InvalidOperationException>(() => book.ToByteArray());
    }
}
