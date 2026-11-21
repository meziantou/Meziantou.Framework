using System.Text;
using Meziantou.Framework.Email.Internals.Ltp;

namespace Meziantou.Framework.Email.Internals;

/// <summary>Encodes an item to write in a file of a single item (.msg, TNEF): its properties, with the ones Outlook expects, its recipients and its attachments. The strings are in Unicode.</summary>
internal sealed class ItemEncoder
{
    public const ushort AttachDataObjectId = 0x3701;

    private const int MaxEmbeddingDepth = 32;
    private const int StoreUnicodeOk = 0x00040000;
    private const ushort StoreSupportMask = 0x340D;
    private const ushort ObjectType = 0x0FFE;
    private const ushort RowId = 0x3000;

    public NameToIdMapWriter Names { get; } = new();

    public Encoding String8Encoding { get; } = PstContext.GetEncoding(1252);

    public EncodedItem Encode(MapiMessageBuilder item) => Encode(item, depth: 0);

    private EncodedItem Encode(MapiMessageBuilder item, int depth)
    {
        if (depth > MaxEmbeddingDepth)
        {
            throw new InvalidOperationException("The embedded messages are nested too deeply.");
        }

        switch (item)
        {
            case MapiContactBuilder contact:
                contact.Complete();
                break;
            case MapiDistributionListBuilder list:
                list.Complete();
                break;
            case MapiAppointmentBuilder appointment:
                appointment.Complete();
                break;
        }

        var properties = item.Properties.Encode(Names, String8Encoding, ansi: false);
        var recipients = new List<List<WriterProperty>>();
        var to = new List<string>();
        var cc = new List<string>();
        var bcc = new List<string>();
        for (var i = 0; i < item.Recipients.Count; i++)
        {
            var recipient = item.Recipients[i];
            var cells = recipient.Properties.Encode(Names, String8Encoding, ansi: false);
            SetDefault(cells, PropertyIds.RecipientType, MapiPropertyType.Integer32, (int)MapiRecipientType.To);
            SetDefault(cells, ObjectType, MapiPropertyType.Integer32, 6); // MAPI_MAILUSER
            SetDefault(cells, PropertyIds.DisplayType, MapiPropertyType.Integer32, 0);
            SetDefault(cells, PropertyIds.EntryId, MapiPropertyType.Binary, EntryIds.CreateOneOff(recipient.DisplayName, recipient.AddressType, recipient.EmailAddress));
            Set(cells, RowId, MapiPropertyType.Integer32, i);
            var name = recipient.DisplayName ?? recipient.EmailAddress ?? "";
            (recipient.RecipientType switch { MapiRecipientType.Cc => cc, MapiRecipientType.Bcc => bcc, MapiRecipientType.Originator => [], _ => to }).Add(name);
            recipients.Add(cells);
        }

        var attachments = new List<EncodedAttachment>();
        for (var i = 0; i < item.Attachments.Count; i++)
        {
            attachments.Add(EncodeAttachment(item.Attachments[i], i, depth));
        }

        var flags = item.Flags;
        if (item.Attachments.Count > 0)
        {
            flags |= MapiMessageFlags.HasAttachments;
        }

        if (item.IsAssociated)
        {
            flags |= MapiMessageFlags.Associated;
        }

        Set(properties, PropertyIds.MessageFlags, MapiPropertyType.Integer32, (int)flags);
        SetDefault(properties, PropertyIds.Importance, MapiPropertyType.Integer32, (int)MapiImportance.Normal);
        SetDefault(properties, PropertyIds.Sensitivity, MapiPropertyType.Integer32, (int)MapiSensitivity.Normal);
        SetDefault(properties, StoreSupportMask, MapiPropertyType.Integer32, StoreUnicodeOk);
        if (item.Recipients.Count > 0)
        {
            SetDefault(properties, PropertyIds.DisplayTo, MapiPropertyType.Unicode, string.Join("; ", to));
            SetDefault(properties, PropertyIds.DisplayCc, MapiPropertyType.Unicode, string.Join("; ", cc));
            SetDefault(properties, PropertyIds.DisplayBcc, MapiPropertyType.Unicode, string.Join("; ", bcc));
        }

        // The values read from streams are read now: the files of a single item are written in memory
        return new EncodedItem(Materialize(properties), recipients.ConvertAll(Materialize), attachments);
    }

    private EncodedAttachment EncodeAttachment(MapiAttachmentBuilder attachment, int index, int depth)
    {
        var properties = attachment.Properties.Encode(Names, String8Encoding, ansi: false);
        EncodedItem? embedded = null;
        if (attachment.EmbeddedMessage is { } message)
        {
            properties.RemoveAll(property => property.Tag.Id == PropertyIds.AttachDataBinary);
            embedded = Encode(message, depth + 1);
            if (!attachment.IsCopy)
            {
                Set(properties, PropertyIds.AttachMethod, MapiPropertyType.Integer32, (int)MapiAttachmentMethod.EmbeddedMessage);
            }
        }
        else if (!attachment.IsCopy)
        {
            var hasContent = properties.Exists(property => property.Tag.Id == PropertyIds.AttachDataBinary);
            SetDefault(properties, PropertyIds.AttachMethod, MapiPropertyType.Integer32, hasContent ? (int)MapiAttachmentMethod.ByValue : (int)MapiAttachmentMethod.None);
        }

        Set(properties, PropertyIds.AttachNumber, MapiPropertyType.Integer32, index);
        SetDefault(properties, PropertyIds.RenderingPosition, MapiPropertyType.Integer32, -1);
        if (attachment.FileName is { } fileName)
        {
            SetDefault(properties, PropertyIds.AttachLongFilename, MapiPropertyType.Unicode, fileName);
            SetDefault(properties, PropertyIds.DisplayName, MapiPropertyType.Unicode, fileName);
            var extension = Path.GetExtension(fileName);
            if (!string.IsNullOrEmpty(extension))
            {
                SetDefault(properties, PropertyIds.AttachExtension, MapiPropertyType.Unicode, extension);
            }
        }

        return new EncodedAttachment(Materialize(properties), embedded);
    }

    private static List<WriterProperty> Materialize(List<WriterProperty> properties)
    {
        // A reference to an object of an Outlook data file cannot be written in a file of a single item: the writers add the embedded messages
        return [.. properties.Where(property => property.Tag.Type != MapiPropertyType.ObjectReference).Select(property => property.Value is not null ? property : new WriterProperty(property.Tag, ReadAll(property.Stream)))];
    }

    private static byte[] ReadAll(Stream? stream)
    {
        if (stream is null)
        {
            return [];
        }

        // The writer does not dispose the streams of the contents (see MapiAttachmentBuilder.SetContent)
        using var result = new MemoryStream();
        stream.CopyTo(result);
        return result.ToArray();
    }

    private void Set(List<WriterProperty> properties, ushort id, MapiPropertyType type, object value)
    {
        properties.RemoveAll(property => property.Tag.Id == id);
        properties.Add(new WriterProperty(new MapiPropertyTag(id, type), PropertyValueCodec.Encode(type, value, String8Encoding)));
    }

    private void SetDefault(List<WriterProperty> properties, ushort id, MapiPropertyType type, object value)
    {
        if (!properties.Exists(property => property.Tag.Id == id))
        {
            Set(properties, id, type, value);
        }
    }
}
