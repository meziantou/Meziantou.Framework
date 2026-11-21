using System.Text;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Email.Internals.Cfb;

namespace Meziantou.Framework.Email.Fuzz;

/// <summary>
/// The .msg reader reads any input or throws an <see cref="EmailException"/>. Writing an item that was read and reading it
/// back is stable: the first copy can lose what an .msg file of the writer cannot express, the next ones change nothing.
/// A compound file the writer writes reads back with the same storages and streams.
/// </summary>
public static partial class EmailFuzzTargets
{
    private const string MsgSeedPrefix = "TestData/Msg/";

    private static readonly string[] MsgTokens =
    [
        BinaryToken([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]), BinaryToken([0x3E, 0x00, 0x03, 0x00, 0xFE, 0xFF, 0x09, 0x00, 0x06, 0x00]), BinaryToken([0x3E, 0x00, 0x04, 0x00, 0xFE, 0xFF, 0x0C, 0x00, 0x06, 0x00]),
        BinaryToken([0xFE, 0xFF, 0xFF, 0xFF]), BinaryToken([0xFD, 0xFF, 0xFF, 0xFF]), BinaryToken([0xFC, 0xFF, 0xFF, 0xFF]), BinaryToken([0x00, 0x10, 0x00, 0x00]),
        Utf16Token("Root Entry"), Utf16Token("__properties_version1.0"), Utf16Token("__nameid_version1.0"), Utf16Token("__recip_version1.0_#00000000"), Utf16Token("__attach_version1.0_#00000000"),
        Utf16Token("__substg1.0_0037001F"), Utf16Token("__substg1.0_1000001F"), Utf16Token("__substg1.0_3701000D"), Utf16Token("__substg1.0_37010102"), Utf16Token("__substg1.0_00030102"), Utf16Token("__substg1.0_8000101F-00000000"),
        BinaryToken([0x1F, 0x00, 0x37, 0x00, 0x06, 0x00, 0x00, 0x00]), BinaryToken([0x03, 0x00, 0x05, 0x37, 0x06, 0x00, 0x00, 0x00]), BinaryToken([0x0D, 0x00, 0x01, 0x37, 0x06, 0x00, 0x00, 0x00]),
    ];

    private static IReadOnlyList<FuzzTarget> MsgTargets { get; } =
    [
        new("msg-read", MsgRead) { Seeds = GetMsgSeeds, Dictionary = MsgTokens },
        new("msg-roundtrip", MsgRoundTrip) { Seeds = GetMsgSeeds, Dictionary = MsgTokens },
        new("cfb-write", CompoundFileWrite) { Seeds = GetCompoundFileWriteSeeds, Dictionary = MsgTokens },
    ];

    private static void MsgRead(ReadOnlySpan<byte> data)
    {
        try
        {
            var item = MsgFile.Load(data.ToArray(), ReadOptions);
            DescribeItem(item, [], "", depth: 0);
        }
        catch (EmailException)
        {
        }
    }

    private static void MsgRoundTrip(ReadOnlySpan<byte> data)
    {
        MapiMessage item;
        var first = new List<string>();
        try
        {
            item = MsgFile.Load(data.ToArray(), ReadOptions);
            item = MsgFile.Load(MsgFile.ToByteArray(MapiMessageBuilder.FromMessage(item)));

            // The values are copied as they are: a malformed value (a compressed RTF body...) is malformed in the copy too
            DescribeItem(item, first, "", depth: 0);
        }
        catch (EmailException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            // A limit of the writer: more than 32,767 named properties, embedded messages nested too deeply...
            return;
        }

        var second = new List<string>();
        DescribeItem(MsgFile.Load(MsgFile.ToByteArray(MapiMessageBuilder.FromMessage(item))), second, "", depth: 0);
        FuzzAssert.Equal(string.Join('\n', first), string.Join('\n', second), "The copy of the copy of an item differs from the copy");
    }

    /// <summary>Writes the storages and streams the input describes in a compound file, and reads them back.</summary>
    private static void CompoundFileWrite(ReadOnlySpan<byte> data)
    {
        var root = new CfbWriterStorage("Root Entry");
        var storages = new List<CfbWriterStorage> { root };
        var count = FuzzInput.ReadByte(ref data);
        for (var i = 0; i < count && !data.IsEmpty; i++)
        {
            var kind = FuzzInput.ReadByte(ref data);
            var parent = FuzzInput.Pick(storages, FuzzInput.ReadByte(ref data));

            // The names differ by their number, and the letters before it test the order of the tree
            var name = new StringBuilder();
            var letters = FuzzInput.ReadByte(ref data) % 12;
            for (var j = 0; j < letters; j++)
            {
                name.Append((char)FuzzInput.Pick("aBcDeFgHiJ_-.é"u8.ToArray().Select(b => (char)b).ToArray(), FuzzInput.ReadByte(ref data)));
            }

            name.Append('#').Append(i);
            if (kind % 4 == 0)
            {
                storages.Add(parent.AddStorage(name.ToString()));
            }
            else
            {
                var size = (FuzzInput.ReadByte(ref data) | (FuzzInput.ReadByte(ref data) << 8)) % 10_000;
                var content = new byte[size];
                var seed = FuzzInput.ReadByte(ref data);
                for (var j = 0; j < content.Length; j++)
                {
                    content[j] = (byte)(seed + (j * 7));
                }

                parent.AddStream(name.ToString(), content);
            }
        }

        using var stream = new MemoryStream();
        CompoundFileWriter.Write(root, stream);
        CompareCompoundFile(root, CompoundFile.Read(stream.ToArray(), long.MaxValue).Root, "");
    }

    private static void CompareCompoundFile(CfbWriterStorage expected, CfbEntry actual, string path)
    {
        FuzzAssert.Equal(expected.Storages.Count + expected.Streams.Count, actual.Children.Count, "The storage '" + path + "' has a different number of entries");
        foreach (var (name, content) in expected.Streams)
        {
            var entry = actual.GetChild(name);
            FuzzAssert.True(entry is { IsStream: true }, "The stream '" + path + "/" + name + "' is missing");
            FuzzAssert.True(entry.ReadStream().AsSpan().SequenceEqual(content), "The stream '" + path + "/" + name + "' differs");
        }

        foreach (var storage in expected.Storages)
        {
            var entry = actual.GetChild(storage.Name);
            FuzzAssert.True(entry is { IsStorage: true }, "The storage '" + path + "/" + storage.Name + "' is missing");
            CompareCompoundFile(storage, entry, path + "/" + storage.Name);
        }
    }

    private static IEnumerable<FuzzSeed> GetMsgSeeds()
    {
        var assembly = typeof(EmailFuzzTargets).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(MsgSeedPrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, name);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return new FuzzSeed(name[MsgSeedPrefix.Length..], memory.ToArray());
        }

        // A file of the writer, with an embedded message and a multi-valued named property
        var embedded = new MapiMessageBuilder { Subject = "Inner", Body = "Inner body" };
        embedded.Attachments.Add(MapiAttachmentBuilder.FromContent("a.txt", "abc"u8.ToArray()));
        var message = new MapiMessageBuilder { Subject = "Hello", Body = "Body", Categories = ["Red", "Blue"] };
        message.Recipients.Add(new MapiRecipientBuilder("Bob", "bob@example.com"));
        message.Attachments.Add(MapiAttachmentBuilder.FromMessage(embedded));
        yield return new FuzzSeed("writer", MsgFile.ToByteArray(message));
    }

    private static IEnumerable<FuzzSeed> GetCompoundFileWriteSeeds()
    {
        yield return new FuzzSeed("empty", new byte[] { 0 });
        yield return new FuzzSeed("streams", new byte[] { 3, 1, 0, 2, 0, 1, 10, 0, 7, 2, 0, 3, 1, 2, 3, 0x10, 0x10, 9, 1, 0, 0, 0x00, 0x20, 4 });
        yield return new FuzzSeed("storages", new byte[] { 4, 0, 0, 1, 2, 0, 0, 0, 3, 1, 1, 1, 0, 1, 3, 4, 2, 0, 10, 0, 0x50, 0, 1 });
    }

    private static string Utf16Token(string value) => BinaryToken(Encoding.Unicode.GetBytes(value));
}
