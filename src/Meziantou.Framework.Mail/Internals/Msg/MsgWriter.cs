using System.Buffers.Binary;
using Meziantou.Framework.Mail.Internals.Cfb;
using Meziantou.Framework.Mail.Internals.Ltp;

namespace Meziantou.Framework.Mail.Internals.Msg;

/// <summary>Writes an item in an .msg file ([MS-OXMSG]): a compound file with a storage per recipient, attachment and embedded message, with Unicode strings.</summary>
internal static class MsgWriter
{
    public static void Write(PstMessageBuilder message, Stream output)
    {
        var encoder = new ItemEncoder();
        var item = encoder.Encode(message);
        var root = new CfbWriterStorage("Root Entry");
        WriteMessage(root, item, depth: 0);

        // The named properties of the file, embedded messages included
        var nameId = root.AddStorage(MsgNames.NameId);
        foreach (var (tag, value) in encoder.Names.CreateProperties(MsgNames.BucketCount))
        {
            if (tag.Type == PstPropertyType.Binary)
            {
                nameId.AddStream(MsgNames.Stream(tag), value);
            }
        }

        CompoundFileWriter.Write(root, output);
    }

    private static void WriteMessage(CfbWriterStorage storage, EncodedItem item, int depth)
    {
        for (var i = 0; i < item.Recipients.Count; i++)
        {
            WriteProperties(storage.AddStorage(MsgNames.Recipient(i)), item.Recipients[i], header: new byte[8]);
        }

        for (var i = 0; i < item.Attachments.Count; i++)
        {
            var attachment = item.Attachments[i];
            var attachmentStorage = storage.AddStorage(MsgNames.Attachment(i));
            var properties = attachment.Properties;
            if (attachment.EmbeddedMessage is { } embedded)
            {
                WriteMessage(attachmentStorage.AddStorage(MsgNames.EmbeddedMessage), embedded, depth + 1);
                properties = [.. properties, new WriterProperty(new PstPropertyTag(MsgNames.AttachDataObjectId, PstPropertyType.ObjectReference), [])];
            }

            WriteProperties(attachmentStorage, properties, header: new byte[8]);
        }

        // [MS-OXMSG] 2.4.1: the header of the message of the file has 8 more reserved bytes than the header of an embedded message
        var header = new byte[depth == 0 ? 32 : 24];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), item.Recipients.Count);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), item.Attachments.Count);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), item.Recipients.Count);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(20), item.Attachments.Count);
        WriteProperties(storage, item.Properties, header);
    }

    /// <summary>Writes the property stream of an object, and a stream per variable-size value ([MS-OXMSG] 2.4.2).</summary>
    private static void WriteProperties(CfbWriterStorage storage, List<WriterProperty> properties, byte[] header)
    {
        using var stream = new MemoryStream();
        stream.Write(header);
        Span<byte> entry = stackalloc byte[16];
        foreach (var property in properties.OrderBy(property => property.Tag.Value))
        {
            var tag = property.Tag;
            var value = property.Value ?? [];
            entry.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(entry, tag.Value);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], 0x6); // PROPATTR_READABLE | PROPATTR_WRITABLE
            if (MsgPropertySource.IsInline(tag.Type))
            {
                value.AsSpan(0, Math.Min(value.Length, 8)).CopyTo(entry[8..]);
            }
            else if (tag.Type == PstPropertyType.ObjectReference)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], 0xFFFFFFFF);
            }
            else if (tag.Type is PstPropertyType.MultipleUnicode or PstPropertyType.MultipleString8 or PstPropertyType.MultipleBinary)
            {
                // A stream of the lengths, then a stream per value
                var values = PropertyValueCodec.SplitVariable(value);
                var lengthSize = tag.Type == PstPropertyType.MultipleBinary ? 8 : 4;
                var lengths = new byte[values.Count * lengthSize];
                for (var i = 0; i < values.Count; i++)
                {
                    var item = AddTerminator(PropertyTypes.GetElementType(tag.Type), values[i]);
                    BinaryPrimitives.WriteInt32LittleEndian(lengths.AsSpan(i * lengthSize), item.Length);
                    storage.AddStream(MsgNames.Stream(tag, i), item);
                }

                storage.AddStream(MsgNames.Stream(tag), lengths);
                BinaryPrimitives.WriteInt32LittleEndian(entry[8..], lengths.Length);
            }
            else
            {
                var data = AddTerminator(tag.Type, value);
                storage.AddStream(MsgNames.Stream(tag), data);
                BinaryPrimitives.WriteInt32LittleEndian(entry[8..], data.Length);
            }

            stream.Write(entry);
        }

        storage.AddStream(MsgNames.Properties, stream.ToArray());
    }

    /// <summary>Adds the null terminator of a string: [MS-OXMSG] 2.1.3 counts it in the size of the value.</summary>
    internal static byte[] AddTerminator(PstPropertyType type, byte[] value) => type switch
    {
        PstPropertyType.Unicode => [.. value, 0, 0],
        PstPropertyType.String8 => [.. value, 0],
        _ => value,
    };
}
