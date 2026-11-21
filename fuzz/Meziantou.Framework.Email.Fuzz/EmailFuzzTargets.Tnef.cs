using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Email.Fuzz;

/// <summary>
/// The TNEF reader reads any input or throws a <see cref="EmailException"/>. Writing an item that was read and reading it
/// back is stable: the first copy can lose what the writer does not write (the attributes of the old versions of Microsoft
/// Mail become properties), the next ones change nothing. A MIME message with the stream as a winmail.dat attachment converts
/// to the model without throwing.
/// </summary>
public static partial class EmailFuzzTargets
{
    private const string TnefSeedPrefix = "TestData/Tnef/";

    private static readonly PstReadOptions TnefOptions = new() { MaxPropertySize = 4 * 1024 * 1024, MaxEmbeddedMessageDepth = 8, VerifyChecksums = false };

    private static readonly string[] TnefTokens =
    [
        BinaryToken([0x78, 0x9F, 0x3E, 0x22]), BinaryToken([0x01, 0x06, 0x90, 0x08, 0x00, 0x04, 0x00, 0x00, 0x00]), BinaryToken([0x01, 0x07, 0x90, 0x06, 0x00]),
        BinaryToken([0x01, 0x03, 0x90, 0x06, 0x00]), BinaryToken([0x01, 0x04, 0x90, 0x06, 0x00]), BinaryToken([0x02, 0x05, 0x90, 0x06, 0x00]), BinaryToken([0x02, 0x02, 0x90, 0x06, 0x00]),
        BinaryToken([0x01, 0x08, 0x80, 0x07, 0x00]), BinaryToken([0x01, 0x04, 0x80, 0x01, 0x00]), BinaryToken([0x02, 0x0F, 0x80, 0x06, 0x00]), BinaryToken([0x02, 0x10, 0x80, 0x01, 0x00]),
        BinaryToken([0x1F, 0x00, 0x37, 0x00]), BinaryToken([0x02, 0x01, 0x01, 0x37]), BinaryToken([0x0D, 0x00, 0x01, 0x37]), BinaryToken([0x1F, 0x10, 0x00, 0x80]), BinaryToken([0x03, 0x00, 0x05, 0x37]),
        BinaryToken([0x07, 0x03, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46]), BinaryToken([0x29, 0x03, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46]),
        "IPM.Microsoft Mail.Note", "IPM.Note",
    ];

    private static IReadOnlyList<FuzzTarget> TnefTargets { get; } =
    [
        new("tnef-read", TnefRead) { Seeds = GetTnefSeeds, Dictionary = TnefTokens },
        new("tnef-roundtrip", TnefRoundTrip) { Seeds = GetTnefSeeds, Dictionary = TnefTokens },
    ];

    private static void TnefRead(ReadOnlySpan<byte> data)
    {
        var bytes = data.ToArray();
        try
        {
            DescribeItem(TnefFile.Load(bytes, TnefOptions), [], "", depth: 0);
        }
        catch (EmailException)
        {
        }

        // A winmail.dat attachment of a MIME message: the conversion keeps a malformed stream as an attachment
        var message = new MimeMessage { Subject = "winmail" };
        var part = new MimePart { ContentType = new MimeContentType("application", "ms-tnef") };
        part.SetContent(bytes);
        message.Body = part;
        try
        {
            _ = EmailMessage.FromMime(message);
        }
        catch (EmailResourceLimitException)
        {
        }
    }

    private static void TnefRoundTrip(ReadOnlySpan<byte> data)
    {
        var first = new List<string>();
        MapiMessage item;
        try
        {
            item = TnefFile.Load(data, TnefOptions);
            item = TnefFile.Load(TnefFile.ToByteArray(MapiMessageBuilder.FromMessage(item)));

            // The values are copied as they are: a malformed value (a compressed RTF body...) is malformed in the copy too
            DescribeItem(item, first, "", depth: 0);
        }
        catch (EmailException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            // A limit of the writer: embedded messages nested too deeply
            return;
        }

        var second = new List<string>();
        DescribeItem(TnefFile.Load(TnefFile.ToByteArray(MapiMessageBuilder.FromMessage(item))), second, "", depth: 0);
        FuzzAssert.Equal(string.Join('\n', first), string.Join('\n', second), "The copy of the copy of an item differs from the copy");
    }

    private static IEnumerable<FuzzSeed> GetTnefSeeds()
    {
        var assembly = typeof(EmailFuzzTargets).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(TnefSeedPrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, name);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return new FuzzSeed(name[TnefSeedPrefix.Length..], memory.ToArray());
        }

        var embedded = new MapiMessageBuilder { Subject = "Inner", Body = "Inner body" };
        embedded.Attachments.Add(MapiAttachmentBuilder.FromContent("a.txt", "abc"u8.ToArray()));
        var message = new MapiMessageBuilder { Subject = "Hello", Body = "Body", Categories = ["Red", "Blue"] };
        message.Recipients.Add(new MapiRecipientBuilder("Bob", "bob@example.com"));
        message.Attachments.Add(MapiAttachmentBuilder.FromMessage(embedded));
        yield return new FuzzSeed("writer", TnefFile.ToByteArray(message));
    }
}
