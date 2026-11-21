using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.Mail.Internals.Cfb;
using Meziantou.Framework.Mail.Internals.Ltp;

namespace Meziantou.Framework.Mail.Internals.Msg;

/// <summary>Writes an item in an .msg file ([MS-OXMSG]): a compound file with a storage per recipient, attachment and embedded message, with Unicode strings.</summary>
internal sealed class MsgWriter
{
    private const int MaxEmbeddingDepth = 32;
    private const int StoreUnicodeOk = 0x00040000;
    private const ushort StoreSupportMask = 0x340D;
    private const ushort ObjectType = 0x0FFE;
    private const ushort RowId = 0x3000;

    private readonly NameToIdMapWriter _names = new();
    private readonly Encoding _string8Encoding = PstContext.GetEncoding(1252);

    public static void Write(PstMessageBuilder message, Stream output)
    {
        var writer = new MsgWriter();
        var root = new CfbWriterStorage("Root Entry");
        writer.WriteMessage(root, message, depth: 0);

        // The named properties of the file, embedded messages included
        var nameId = root.AddStorage(MsgNames.NameId);
        foreach (var (tag, value) in writer._names.CreateProperties(MsgNames.BucketCount))
        {
            if (tag.Type == PstPropertyType.Binary)
            {
                nameId.AddStream(MsgNames.Stream(tag), value);
            }
        }

        CompoundFileWriter.Write(root, output);
    }

    private void WriteMessage(CfbWriterStorage storage, PstMessageBuilder item, int depth)
    {
        if (depth > MaxEmbeddingDepth)
        {
            throw new InvalidOperationException("The embedded messages are nested too deeply.");
        }

        switch (item)
        {
            case PstContactBuilder contact:
                contact.Complete();
                break;
            case PstDistributionListBuilder list:
                list.Complete();
                break;
            case PstAppointmentBuilder appointment:
                appointment.Complete();
                break;
        }

        var properties = item.Properties.Encode(_names, _string8Encoding, ansi: false);
        var to = new List<string>();
        var cc = new List<string>();
        var bcc = new List<string>();
        for (var i = 0; i < item.Recipients.Count; i++)
        {
            var recipient = item.Recipients[i];
            var cells = recipient.Properties.Encode(_names, _string8Encoding, ansi: false);
            SetDefault(cells, PropertyIds.RecipientType, PstPropertyType.Integer32, (int)PstRecipientType.To);
            SetDefault(cells, ObjectType, PstPropertyType.Integer32, 6); // MAPI_MAILUSER
            SetDefault(cells, PropertyIds.DisplayType, PstPropertyType.Integer32, 0);
            SetDefault(cells, PropertyIds.EntryId, PstPropertyType.Binary, EntryIds.CreateOneOff(recipient.DisplayName, recipient.AddressType, recipient.EmailAddress));
            Set(cells, RowId, PstPropertyType.Integer32, i);
            var name = recipient.DisplayName ?? recipient.EmailAddress ?? "";
            (recipient.RecipientType switch { PstRecipientType.Cc => cc, PstRecipientType.Bcc => bcc, PstRecipientType.Originator => [], _ => to }).Add(name);
            WriteProperties(storage.AddStorage(MsgNames.Recipient(i)), cells, header: new byte[8]);
        }

        for (var i = 0; i < item.Attachments.Count; i++)
        {
            WriteAttachment(storage.AddStorage(MsgNames.Attachment(i)), item.Attachments[i], i, depth);
        }

        var flags = item.Flags;
        if (item.Attachments.Count > 0)
        {
            flags |= PstMessageFlags.HasAttachments;
        }

        if (item.IsAssociated)
        {
            flags |= PstMessageFlags.Associated;
        }

        Set(properties, PropertyIds.MessageFlags, PstPropertyType.Integer32, (int)flags);
        SetDefault(properties, PropertyIds.Importance, PstPropertyType.Integer32, (int)PstImportance.Normal);
        SetDefault(properties, PropertyIds.Sensitivity, PstPropertyType.Integer32, (int)PstSensitivity.Normal);
        SetDefault(properties, StoreSupportMask, PstPropertyType.Integer32, StoreUnicodeOk);
        if (item.Recipients.Count > 0)
        {
            SetDefault(properties, PropertyIds.DisplayTo, PstPropertyType.Unicode, string.Join("; ", to));
            SetDefault(properties, PropertyIds.DisplayCc, PstPropertyType.Unicode, string.Join("; ", cc));
            SetDefault(properties, PropertyIds.DisplayBcc, PstPropertyType.Unicode, string.Join("; ", bcc));
        }

        // [MS-OXMSG] 2.4.1: the header of the message of the file has 8 more reserved bytes than the header of an embedded message
        var header = new byte[depth == 0 ? 32 : 24];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), item.Recipients.Count);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), item.Attachments.Count);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), item.Recipients.Count);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(20), item.Attachments.Count);
        WriteProperties(storage, properties, header);
    }

    private void WriteAttachment(CfbWriterStorage storage, PstAttachmentBuilder attachment, int index, int depth)
    {
        var properties = attachment.Properties.Encode(_names, _string8Encoding, ansi: false);
        if (attachment.EmbeddedMessage is { } embedded)
        {
            properties.RemoveAll(property => property.Tag.Id == PropertyIds.AttachDataBinary);
            WriteMessage(storage.AddStorage(MsgNames.EmbeddedMessage), embedded, depth + 1);
            properties.Add(new WriterProperty(new PstPropertyTag(MsgNames.AttachDataObjectId, PstPropertyType.ObjectReference), []));
            if (!attachment.IsCopy)
            {
                Set(properties, PropertyIds.AttachMethod, PstPropertyType.Integer32, (int)PstAttachmentMethod.EmbeddedMessage);
            }
        }
        else if (!attachment.IsCopy)
        {
            var hasContent = properties.Exists(property => property.Tag.Id == PropertyIds.AttachDataBinary);
            SetDefault(properties, PropertyIds.AttachMethod, PstPropertyType.Integer32, hasContent ? (int)PstAttachmentMethod.ByValue : (int)PstAttachmentMethod.None);
        }

        Set(properties, PropertyIds.AttachNumber, PstPropertyType.Integer32, index);
        SetDefault(properties, PropertyIds.RenderingPosition, PstPropertyType.Integer32, -1);
        if (attachment.FileName is { } fileName)
        {
            SetDefault(properties, PropertyIds.AttachLongFilename, PstPropertyType.Unicode, fileName);
            SetDefault(properties, PropertyIds.DisplayName, PstPropertyType.Unicode, fileName);
            var extension = Path.GetExtension(fileName);
            if (!string.IsNullOrEmpty(extension))
            {
                SetDefault(properties, PropertyIds.AttachExtension, PstPropertyType.Unicode, extension);
            }
        }

        WriteProperties(storage, properties, header: new byte[8]);
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
            if (tag.Type == PstPropertyType.ObjectReference && tag.Id != MsgNames.AttachDataObjectId)
            {
                // A reference to an object of an Outlook data file, which an .msg file cannot store
                continue;
            }

            var value = property.Value ?? ReadAll(property.Stream);
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
    private static byte[] AddTerminator(PstPropertyType type, byte[] value) => type switch
    {
        PstPropertyType.Unicode => [.. value, 0, 0],
        PstPropertyType.String8 => [.. value, 0],
        _ => value,
    };

    private static byte[] ReadAll(Stream? stream)
    {
        if (stream is null)
        {
            return [];
        }

        // The writer does not dispose the streams of the contents (see PstAttachmentBuilder.SetContent)
        using var result = new MemoryStream();
        stream.CopyTo(result);
        return result.ToArray();
    }

    private void Set(List<WriterProperty> properties, ushort id, PstPropertyType type, object value)
    {
        properties.RemoveAll(property => property.Tag.Id == id);
        properties.Add(new WriterProperty(new PstPropertyTag(id, type), PropertyValueCodec.Encode(type, value, _string8Encoding)));
    }

    private void SetDefault(List<WriterProperty> properties, ushort id, PstPropertyType type, object value)
    {
        if (!properties.Exists(property => property.Tag.Id == id))
        {
            Set(properties, id, type, value);
        }
    }
}
