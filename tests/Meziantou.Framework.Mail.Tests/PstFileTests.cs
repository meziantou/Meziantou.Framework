using System.Text;
using Meziantou.Framework.Mail.Internals;
using Meziantou.Framework.Mail.Internals.Ndb;

namespace Meziantou.Framework.Mail.Tests;

public sealed class PstFileTests
{
    private static string GetPath(string fileName) => PstOracleTests.GetPath(fileName);

    [Fact]
    public void Open_Snippet()
    {
        var path = GetPath("tika-variousBodyTypes.pst");

        #region PstFile_Open
        using var file = PstFile.Open(path);
        Assert.Equal(PstFormat.Unicode, file.Format);
        Assert.Equal("Outlook Data File", file.Store.DisplayName);

        var folder = file.GetFolder("Inbox/tmp");
        Assert.NotNull(folder);
        Assert.Equal(4, folder.ContentCount);
        #endregion
    }

    [Fact]
    public void EnumerateItems_Snippet()
    {
        var path = GetPath("tika-variousBodyTypes.pst");

        #region PstFolder_EnumerateItems
        using var file = PstFile.Open(path);
        var subjects = new List<string>();
        void Visit(PstFolder folder)
        {
            foreach (var item in folder.EnumerateItems())
            {
                subjects.Add($"{folder.Path}: {item.Subject}");
            }

            foreach (var child in folder.Folders)
            {
                Visit(child);
            }
        }

        Visit(file.RootFolder);
        Assert.Contains("Top of Outlook data file/Inbox/tmp: original email", subjects);
        #endregion
    }

    [Fact]
    public void ReadMessage_Snippet()
    {
        var path = GetPath("libyal-outlook.pst");

        #region PstMessage_Read
        using var file = PstFile.Open(path);
        var message = file.GetFolder("Inbox")!.EnumerateItems().First();
        Assert.Equal("Welcome to Microsoft Office Outlook 2003", message.Subject);
        Assert.Equal("Outlook 2003 Team", message.SenderName);
        Assert.Single(message.Recipients);
        Assert.HasCount(11, message.Attachments);
        Assert.Contains("Outlook", message.Body, StringComparison.Ordinal);
        #endregion
    }

    [Fact]
    public void SaveAttachment_Snippet()
    {
        var path = GetPath("libyal-outlook.pst");
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            #region PstAttachment_Save
            using var file = PstFile.Open(path);
            var message = file.GetFolder("Inbox")!.EnumerateItems().First();
            foreach (var attachment in message.Attachments)
            {
                if (attachment.Method == PstAttachmentMethod.ByValue)
                {
                    // Read the content block by block, without loading it in memory
                    using var content = attachment.OpenRead();
                    using var output = File.Create(Path.Combine(directory.FullName, Path.GetFileName(attachment.Name ?? "attachment")));
                    content.CopyTo(output);
                }
            }
            #endregion

            Assert.HasCount(11, directory.GetFiles());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ReadAppointment_Snippet()
    {
        var path = GetPath("java-libpst-dist-list.pst");

        #region PstAppointment_Read
        using var file = PstFile.Open(path);
        var appointment = file.GetFolder("Calendar")!.EnumerateItems().OfType<PstAppointment>().Single();
        Assert.Equal("Test appointment", appointment.Subject);
        Assert.Equal(new DateTime(2016, 8, 2, 15, 0, 0, DateTimeKind.Utc), appointment.Start);

        // The recurrence: every Tuesday, with deleted and moved occurrences
        var recurrence = appointment.GetRecurrence()!;
        Assert.Equal(PstRecurrenceFrequency.Weekly, recurrence.Frequency);
        Assert.Equal(PstDaysOfWeek.Tuesday, recurrence.DaysOfWeek);
        Assert.HasCount(3, recurrence.DeletedInstanceDates);
        Assert.HasCount(2, recurrence.Exceptions);
        Assert.Equal(new DateTime(2016, 8, 23, 9, 0, 0), recurrence.Exceptions[0].StartDateTime);

        // The RTF body, decompressed from the compressed RTF property
        var rtf = Encoding.ASCII.GetString(appointment.GetRtfBody()!);
        Assert.StartsWith(@"{\rtf1", rtf, StringComparison.Ordinal);
        #endregion
    }

    [Fact]
    public void ReadContact_Snippet()
    {
        var path = GetPath("java-libpst-dist-list.pst");

        #region PstContact_Read
        using var file = PstFile.Open(path);
        foreach (var item in file.GetFolder("Contacts")!.EnumerateItems())
        {
            switch (item)
            {
                case PstContact contact:
                    Assert.Equal("contact", contact.GivenName);
                    Assert.Equal("contact1@rjohnson.id.au", contact.Email1Address);
                    break;

                case PstDistributionList list:
                    Assert.Equal("test dist list", list.Name);
                    Assert.Equal("dist1@rjohnson.id.au", list.Members[1].EmailAddress);
                    break;
            }
        }
        #endregion
    }

    [Fact]
    public void ReadProperties_Snippet()
    {
        var path = GetPath("java-libpst-dist-list.pst");

        #region PstPropertyCollection_Read
        using var file = PstFile.Open(path);
        var appointment = file.GetFolder("Calendar")!.EnumerateItems().Single();

        // A property from its identifier (PidTagMessageClass, 0x001A), whatever its type
        Assert.Equal("IPM.Appointment", appointment.Properties.GetValueOrDefault<string>(0x001A));

        // A named property from its property set and its identifier (PidLidLocation of PSETID_Appointment)
        var location = new PstNamedProperty(PstPropertySets.Appointment, 0x8208);
        Assert.NotNull(appointment.Properties.Find(location));

        // All the properties, with their tags, their names and their values
        foreach (var property in appointment.Properties)
        {
            Assert.NotEqual(PstPropertyType.Unspecified, property.Type);
        }
        #endregion
    }

    [Fact]
    public void Validate_Snippet()
    {
        var path = GetPath("outlook-pst-rs-Empty.pst");

        #region PstFile_Validate
        using var file = PstFile.Open(path);
        var issues = file.Validate();
        Assert.Empty(issues);
        #endregion
    }

    [Fact]
    public void ReadOptions_Snippet()
    {
        var path = GetPath("libyal-outlook.pst");

        #region PstReadOptions_Limits
        var options = new PstReadOptions
        {
            MaxPropertySize = 16 * 1024 * 1024,
            MaxEmbeddedMessageDepth = 4,
        };

        try
        {
            using var file = PstFile.Open(path, options);
            Assert.NotEmpty(file.RootFolder.Folders);
        }
        catch (PstException)
        {
            // InvalidPstException, PstResourceLimitException or UnsupportedPstFeatureException
        }
        #endregion
    }

    [Fact]
    public void DistributionList_Members()
    {
        using var file = PstFile.Open(GetPath("java-libpst-dist-list.pst"));
        var list = file.GetFolder("Contacts")!.EnumerateItems().OfType<PstDistributionList>().Single();
        Assert.Equal("test dist list", list.Name);
        Assert.Equal(["contact1@rjohnson.id.au", "dist1@rjohnson.id.au", "dist2@rjohnson.id.au"], list.Members.Select(member => member.EmailAddress));
        Assert.All(list.Members, member => Assert.Equal("SMTP", member.AddressType));
    }

    [Fact]
    public void Contact_Properties()
    {
        using var file = PstFile.Open(GetPath("java-libpst-dist-list.pst"));
        var contact = file.GetFolder("Contacts")!.EnumerateItems().OfType<PstContact>().Single();
        Assert.Equal("contact", contact.GivenName);
        Assert.Equal("1", contact.Surname);
        Assert.Equal("contact1@rjohnson.id.au", contact.Email1Address);
        Assert.Equal(["contact1@rjohnson.id.au"], contact.EmailAddresses);
    }

    [Fact]
    public void Store_PasswordFlag()
    {
        using var file = PstFile.Open(GetPath("java-libpst-passworded.pst"));
        Assert.True(file.Store.HasPassword);
        Assert.NotNull(file.GetFolder("Contacts"));
    }

    [Fact]
    public void Ost4K_CompressedBlocks()
    {
        using var file = PstFile.Open(GetPath("java-libpst-example-2013.ost"));
        Assert.Equal(PstFormat.Unicode4K, file.Format);
        Assert.True(file.IsOfflineFile);
        var inbox = file.GetFolder("Root - Mailbox/IPM_SUBTREE/Inbox");
        Assert.NotNull(inbox);
        Assert.All(inbox.EnumerateItems(), item => Assert.Equal("Microsoft Outlook Test Message", item.Subject));
    }

    [Fact]
    public void Open_NotAPstFile()
    {
        Assert.Throws<InvalidPstException>(() => PstFile.Open(new byte[1024]));
        Assert.Throws<InvalidPstException>(() => PstFile.Open("!BDN"u8.ToArray()));
    }

    [Fact]
    public void Open_UnsupportedVersion()
    {
        var data = File.ReadAllBytes(GetPath("outlook-pst-rs-Empty.pst"));
        data[10] = 99;
        Assert.Throws<UnsupportedPstFeatureException>(() => PstFile.Open(data));
    }

    [Fact]
    public void Open_HeaderCrcMismatch()
    {
        var data = File.ReadAllBytes(GetPath("outlook-pst-rs-Empty.pst"));
        data[40]++;
        Assert.Throws<InvalidPstException>(() => PstFile.Open(data));
        using var file = PstFile.Open(data, new PstReadOptions { VerifyChecksums = false });
        Assert.Equal("Empty", file.Store.DisplayName);
    }

    [Fact]
    public void Read_TruncatedFile()
    {
        var data = File.ReadAllBytes(GetPath("tika-variousBodyTypes.pst"))[..40000];
        Assert.Throws<InvalidPstException>(() =>
        {
            using var file = PstFile.Open(data);
            _ = file.Store;
        });
    }

    [Fact]
    public void Validate_ReportsCorruptedBlock()
    {
        var data = PstWriterTests.WriteSmallSample(PstFormat.Unicode, PstEncryption.None);
        using (var file = PstFile.Open(data))
        {
            Assert.Empty(file.Validate());
        }

        // Change a byte of the data of the first block: its CRC does not match anymore
        using (var file = PstFile.Open(data))
        {
            var block = file.Reader.EnumerateBlockEntries().First();
            data[(int)block.Ib + 2] ^= 0xFF;
        }

        using var corrupted = PstFile.Open(data);
        Assert.NotEmpty(corrupted.Validate());
    }

    [Fact]
    public void Read_ResourceLimits()
    {
        var data = PstWriterTests.WriteSample(PstFormat.Unicode, PstEncryption.Permute);
        using var file = PstFile.Open(data, new PstReadOptions { MaxPropertySize = 50_000 });
        var message = file.GetFolder("Inbox")!.EnumerateItems().First();
        Assert.Throws<PstResourceLimitException>(() => message.Attachments[1].GetContent());

        // Streams are not limited
        using var stream = message.Attachments[1].OpenRead();
        Assert.Equal(100_000, stream.Length);
    }

    [Fact]
    public void Crc_MatchesSpecification()
    {
        // [MS-OXRTFCP] 3.3.1: the CRC of the content of the first example
        byte[] data = [0x03, 0x00, 0x0A, 0x00, 0x72, 0x63, 0x70, 0x67, 0x31, 0x32, 0x35, 0x42, 0x32, 0x0A, 0xF3, 0x20, 0x68, 0x65, 0x6C, 0x09, 0x00, 0x20, 0x62, 0x77, 0x05, 0xB0, 0x6C, 0x64, 0x7D, 0x0A, 0x80, 0x0F, 0xA0];
        Assert.Equal(0xA7C7C5F1u, PstCrc.Compute(data));
    }

    [Fact]
    public void Crypt_PermuteRoundTrip()
    {
        var data = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var copy = data.ToArray();
        PstCrypt.Encode(PstEncryption.Permute, copy, 4);
        Assert.NotEqual(data, copy);
        PstCrypt.Decode(PstEncryption.Permute, copy, 4);
        Assert.Equal(data, copy);

        PstCrypt.Encode(PstEncryption.Cyclic, copy, 0x12345678);
        Assert.NotEqual(data, copy);
        PstCrypt.Decode(PstEncryption.Cyclic, copy, 0x12345678);
        Assert.Equal(data, copy);
    }

    [Theory]
    [InlineData("2D0000002B0000004C5A4675F1C5C7A703000A007263706731323542320AF32068656C090020627705B06C647D0A800FA0", "{\\rtf1\\ansi\\ansicpg1252\\pard hello world}\r\n")]
    [InlineData("1A0000001C0000004C5A4675E2D44B51410004205758595A0D6E7D010EB0", "{\\rtf1 WXYZWXYZWXYZWXYZWXYZ}")]
    public void Lzfu_Decompress_SpecificationExamples(string compressed, string expected)
    {
        // [MS-OXRTFCP] 3.1.1 and 3.1.2
        var data = Convert.FromHexString(compressed.Replace(" ", "", StringComparison.Ordinal));
        Assert.Equal(expected, Encoding.ASCII.GetString(Lzfu.Decompress(data, 1000)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\\rtf1\\ansi\\ansicpg1252\\pard hello world}\r\n")]
    [InlineData("{\\rtf1 WXYZWXYZWXYZWXYZWXYZ}")]
    public void Lzfu_CompressRoundTrip(string rtf)
    {
        var bytes = Encoding.ASCII.GetBytes(rtf + new string('a', rtf.Length * 100));
        Assert.Equal(bytes, Lzfu.Decompress(Lzfu.Compress(bytes), int.MaxValue));
        Assert.Equal(bytes, Lzfu.Decompress(Lzfu.Store(bytes), int.MaxValue));
    }

    [Fact]
    public void DistributionList_SpecificationExample()
    {
        // [MS-OXOCNTC] 4.2: a one-off member and a wrapped member, and the checksum of the members
        var oneOff = Convert.FromHexString("00000000812b1fa4bea310199d6e00dd010f540200000180540068006f006d00610073002000480061006d0062006f0072006700000053004d005400500000007500730065007200310040006500780061006d0070006c0065002e0063006f006d000000");
        var wrapped = Convert.FromHexString("00000000c091add3519dcf11a4a900aa0047faa4c300000000c242428260001647ada95c0777b74be20700612a7bab49f64e4b9c52dbfb5a53aa1c000000f04ea60000612a7bab49f64e4b9c52dbfb5a53aa1c000000f0d5b00000");
        var member = EntryIds.ParseOneOff(oneOff)!;
        Assert.Equal("Thomas Hamborg", member.DisplayName);
        Assert.Equal("SMTP", member.AddressType);
        Assert.Equal("user1@example.com", member.EmailAddress);
        Assert.Null(EntryIds.ParseWrappedOneOff(wrapped));
        Assert.Equal(0xD4B0223Au, PstCrc.Compute(wrapped, PstCrc.Compute(oneOff)));
        Assert.Equal(oneOff, EntryIds.CreateOneOff("Thomas Hamborg", "SMTP", "user1@example.com"));
    }
}
