using System.Text;
using Meziantou.Framework.Mail.Internals;
using Meziantou.Framework.Mail.Internals.Ltp;
using Meziantou.Framework.Mail.Internals.Ndb;

namespace Meziantou.Framework.Mail;

/// <summary>An item of an Outlook data file: an email, or the base of the typed items (contacts, appointments, tasks, distribution lists).</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Mail.Tests/PstFileTests.cs" ref:region="PstMessage_Read" ref:dedent="true">
/// using var file = PstFile.Open(path);
/// var message = file.GetFolder("Inbox")!.EnumerateItems().First();
/// Assert.Equal("Welcome to Microsoft Office Outlook 2003", message.Subject);
/// Assert.Equal("Outlook 2003 Team", message.SenderName);
/// Assert.Single(message.Recipients);
/// Assert.HasCount(11, message.Attachments);
/// Assert.Contains("Outlook", message.Body, StringComparison.Ordinal);
/// </code>
/// </example>
public class PstMessage
{
    private static readonly PstNamedProperty KeywordsProperty = new(PstPropertySets.PublicStrings, "Keywords");

    private IReadOnlyList<PstRecipient>? _recipients;
    private IReadOnlyList<PstAttachment>? _attachments;

    internal PstMessage(PstContext context, NdbNode node, PstPropertyCollection properties, int embeddingDepth)
    {
        Context = context;
        Node = node;
        EmbeddingDepth = embeddingDepth;
        Properties = properties;
    }

    internal PstContext Context { get; }

    internal NdbNode Node { get; }

    internal int EmbeddingDepth { get; }

    /// <summary>Gets the identifier of the node of the item in the file. For an embedded message, it is the identifier of a subnode of its attachment.</summary>
    public uint NodeId => Node.Nid;

    /// <summary>Gets whether the item is an embedded message, the content of an attachment.</summary>
    public bool IsEmbedded => EmbeddingDepth > 0;

    /// <summary>Gets whether the item is an associated (hidden) item of its folder.</summary>
    public bool IsAssociated => Node.NidType == NidType.AssociatedMessage;

    /// <summary>Gets the properties of the item.</summary>
    public PstPropertyCollection Properties { get; }

    /// <summary>Gets the class of the item, such as <c>IPM.Note</c>, <c>IPM.Contact</c> or <c>IPM.Appointment</c>.</summary>
    public string? MessageClass => Properties.GetString(PropertyIds.MessageClass);

    /// <summary>Gets the subject, without the control characters Outlook stores before the prefix (such as <c>RE: </c>).</summary>
    public string? Subject
    {
        get
        {
            var subject = Properties.GetString(PropertyIds.Subject);

            // [MS-OXCMSG] 2.2.1.46: a subject that starts with 0x01 has the length of its prefix in the second character
            if (subject is { Length: >= 2 } && subject[0] == '\u0001')
            {
                return subject[2..];
            }

            return subject;
        }
    }

    /// <summary>Gets the subject without its prefix, the topic of the conversation.</summary>
    public string? ConversationTopic => Properties.GetString(PropertyIds.ConversationTopic);

    /// <summary>Gets the display name of the sender.</summary>
    public string? SenderName => Properties.GetString(PropertyIds.SenderName);

    /// <summary>Gets the address of the sender, in the format of <see cref="SenderAddressType"/>.</summary>
    public string? SenderEmailAddress => Properties.GetString(PropertyIds.SenderEmailAddress);

    /// <summary>Gets the type of the address of the sender, such as <c>SMTP</c> or <c>EX</c>.</summary>
    public string? SenderAddressType => Properties.GetString(PropertyIds.SenderAddressType);

    /// <summary>Gets the SMTP address of the sender, or the address of the sender when its type is <c>SMTP</c>.</summary>
    public string? SenderSmtpAddress => Properties.GetString(PropertyIds.SenderSmtpAddress) ?? (string.Equals(SenderAddressType, "SMTP", StringComparison.OrdinalIgnoreCase) ? SenderEmailAddress : null);

    /// <summary>Gets the display name of the user the message was sent on behalf of.</summary>
    public string? SentRepresentingName => Properties.GetString(PropertyIds.SentRepresentingName);

    /// <summary>Gets the address of the user the message was sent on behalf of.</summary>
    public string? SentRepresentingEmailAddress => Properties.GetString(PropertyIds.SentRepresentingEmailAddress);

    /// <summary>Gets the display names of the To recipients, separated by semicolons.</summary>
    public string? DisplayTo => Properties.GetString(PropertyIds.DisplayTo);

    /// <summary>Gets the display names of the Cc recipients, separated by semicolons.</summary>
    public string? DisplayCc => Properties.GetString(PropertyIds.DisplayCc);

    /// <summary>Gets the display names of the Bcc recipients, separated by semicolons.</summary>
    public string? DisplayBcc => Properties.GetString(PropertyIds.DisplayBcc);

    /// <summary>Gets the date and time, in UTC, when the message was sent.</summary>
    public DateTime? ClientSubmitTime => Properties.GetDateTime(PropertyIds.ClientSubmitTime);

    /// <summary>Gets the date and time, in UTC, when the message was received.</summary>
    public DateTime? MessageDeliveryTime => Properties.GetDateTime(PropertyIds.MessageDeliveryTime);

    /// <summary>Gets the date and time, in UTC, when the item was created.</summary>
    public DateTime? CreationTime => Properties.GetDateTime(PropertyIds.CreationTime);

    /// <summary>Gets the date and time, in UTC, when the item was last modified.</summary>
    public DateTime? LastModificationTime => Properties.GetDateTime(PropertyIds.LastModificationTime);

    /// <summary>Gets the importance of the item.</summary>
    public PstImportance Importance => (PstImportance)(Properties.GetInt32(PropertyIds.Importance) ?? (int)PstImportance.Normal);

    /// <summary>Gets the sensitivity of the item.</summary>
    public PstSensitivity Sensitivity => (PstSensitivity)(Properties.GetInt32(PropertyIds.Sensitivity) ?? 0);

    /// <summary>Gets the flags of the message, such as read or unsent.</summary>
    public PstMessageFlags Flags => (PstMessageFlags)(Properties.GetInt32(PropertyIds.MessageFlags) ?? (int)PstMessageFlags.None);

    /// <summary>Gets whether the message has been read.</summary>
    public bool IsRead => (Flags & PstMessageFlags.Read) != PstMessageFlags.None;

    /// <summary>Gets the size of the message, as stored in the file.</summary>
    public int? Size => Properties.GetInt32(PropertyIds.MessageSize);

    /// <summary>Gets the Message-ID header of the message.</summary>
    public string? InternetMessageId => Properties.GetString(PropertyIds.InternetMessageId);

    /// <summary>Gets the In-Reply-To header of the message.</summary>
    public string? InReplyToId => Properties.GetString(PropertyIds.InReplyToId);

    /// <summary>Gets the References header of the message.</summary>
    public string? InternetReferences => Properties.GetString(PropertyIds.InternetReferences);

    /// <summary>Gets the headers of the message as received by the transport.</summary>
    public string? TransportMessageHeaders => Properties.GetString(PropertyIds.TransportMessageHeaders);

    /// <summary>Gets the categories of the item.</summary>
    public IReadOnlyList<string> Categories => Properties.GetValueOrDefault<string[]>(KeywordsProperty) ?? [];

    /// <summary>Gets the plain text body.</summary>
    public string? Body => Properties.GetString(PropertyIds.Body);

    /// <summary>Gets the HTML body, decoded with the code page of the message.</summary>
    public string? BodyHtml
    {
        get
        {
            var property = Properties.Find(PropertyIds.Html);
            return property?.Value switch
            {
                string value => value,
                byte[] bytes => DecodeHtml(bytes),
                _ => null,
            };
        }
    }

    /// <summary>Gets whether the message has a compressed RTF body.</summary>
    public bool HasRtfBody => Properties.Find(PropertyIds.RtfCompressed) is not null;

    /// <summary>Gets whether the item has attachments, according to the attachment table.</summary>
    public bool HasAttachments => Attachments.Count > 0;

    /// <summary>Gets the recipients of the message.</summary>
    /// <exception cref="InvalidPstException">The recipient table is malformed.</exception>
    public IReadOnlyList<PstRecipient> Recipients => _recipients ??= LoadRecipients();

    /// <summary>Gets the attachments of the item.</summary>
    /// <exception cref="InvalidPstException">The attachment table is malformed.</exception>
    public IReadOnlyList<PstAttachment> Attachments => _attachments ??= LoadAttachments();

    /// <summary>Returns the RTF body, decompressed from the compressed RTF property.</summary>
    /// <returns>The RTF, or <see langword="null"/> when the message has no RTF body.</returns>
    /// <exception cref="InvalidPstException">The compressed RTF is malformed.</exception>
    public byte[]? GetRtfBody()
    {
        var property = Properties.Find(PropertyIds.RtfCompressed);
        if (property is null || property.Type != PstPropertyType.Binary)
        {
            return null;
        }

        return Lzfu.Decompress(property.GetBytes(), Context.Options.MaxPropertySize, Context.Options.VerifyChecksums);
    }

    /// <summary>Returns the subject.</summary>
    /// <returns>The subject.</returns>
    public override string ToString() => Subject ?? "";

    internal static PstMessage Load(PstContext context, NdbNode node, int embeddingDepth)
    {
        if (embeddingDepth > context.Options.MaxEmbeddedMessageDepth)
        {
            throw new PstResourceLimitException($"The embedded messages are nested deeper than the limit of {context.Options.MaxEmbeddedMessageDepth} levels.");
        }

        var pc = new PropertyContext(node);
        var properties = new PstPropertyCollection(new PropertySource(context, pc.Heap, node), pc.Properties);
        var messageClass = properties.GetString(PropertyIds.MessageClass) ?? "";
        if (IsClass(messageClass, "IPM.Contact"))
        {
            return new PstContact(context, node, properties, embeddingDepth);
        }

        if (IsClass(messageClass, "IPM.DistList"))
        {
            return new PstDistributionList(context, node, properties, embeddingDepth);
        }

        if (IsClass(messageClass, "IPM.Appointment"))
        {
            return new PstAppointment(context, node, properties, embeddingDepth);
        }

        if (IsClass(messageClass, "IPM.Task"))
        {
            return new PstTask(context, node, properties, embeddingDepth);
        }

        return new PstMessage(context, node, properties, embeddingDepth);
    }

    private static bool IsClass(string messageClass, string expected)
    {
        return messageClass.Equals(expected, StringComparison.OrdinalIgnoreCase)
            || (messageClass.Length > expected.Length && messageClass[expected.Length] == '.' && messageClass.StartsWith(expected, StringComparison.OrdinalIgnoreCase));
    }

    private string DecodeHtml(byte[] bytes)
    {
        var encoding = Properties.GetInt32(PropertyIds.InternetCodepage) is int codePage and > 0 and < 65536 ? PstContext.GetEncoding(codePage) : Encoding.UTF8;
        return encoding.GetString(bytes).TrimEnd('\0');
    }

    private List<PstRecipient> LoadRecipients()
    {
        var result = new List<PstRecipient>();
        var node = Node.GetSubnode(Nids.RecipientTable);
        if (node is null)
        {
            return result;
        }

        var table = new TableContext(node);
        var source = new PropertySource(Context, table.Heap, node);
        for (var i = 0; i < table.RowCount; i++)
        {
            result.Add(new PstRecipient(new PstPropertyCollection(source, table.GetRow(i))));
        }

        return result;
    }

    private List<PstAttachment> LoadAttachments()
    {
        var result = new List<PstAttachment>();
        var node = Node.GetSubnode(Nids.AttachmentTable);
        if (node is null)
        {
            return result;
        }

        var table = new TableContext(node);
        for (var i = 0; i < table.RowCount; i++)
        {
            var nid = table.GetRowId(i);
            var attachmentNode = Node.GetSubnode(nid) ?? throw new InvalidPstException($"The attachment 0x{nid:X} of message 0x{Node.Nid:X} does not exist.");
            result.Add(new PstAttachment(this, attachmentNode));
        }

        return result;
    }
}
