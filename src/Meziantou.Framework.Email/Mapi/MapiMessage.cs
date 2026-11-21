using System.Text;
using Meziantou.Framework.Email.Internals;
using Meziantou.Framework.Email.Internals.Ltp;
using Meziantou.Framework.Email.Internals.Ndb;

namespace Meziantou.Framework.Email.Mapi;

/// <summary>An item of an Outlook data file: an email, or the base of the typed items (contacts, appointments, tasks, distribution lists).</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Email.Tests/PstFileTests.cs" ref:region="PstMessage_Read" ref:dedent="true">
/// using var file = PstFile.Open(path);
/// var message = file.GetFolder("Inbox")!.EnumerateItems().First();
/// Assert.Equal("Welcome to Microsoft Office Outlook 2003", message.Subject);
/// Assert.Equal("Outlook 2003 Team", message.SenderName);
/// Assert.Single(message.Recipients);
/// Assert.HasCount(11, message.Attachments);
/// Assert.Contains("Outlook", message.Body, StringComparison.Ordinal);
/// </code>
/// </example>
public class MapiMessage
{
    private static readonly MapiNamedProperty KeywordsProperty = new(MapiPropertySets.PublicStrings, "Keywords");

    private IReadOnlyList<MapiRecipient>? _recipients;
    private IReadOnlyList<MapiAttachment>? _attachments;

    internal MapiMessage(MessageSource source, MapiPropertyCollection properties, int embeddingDepth)
    {
        Source = source;
        EmbeddingDepth = embeddingDepth;
        Properties = properties;
    }

    internal MessageSource Source { get; }

    internal int EmbeddingDepth { get; }

    /// <summary>Gets the identifier of the node of the item in the Outlook data file. For an embedded message, it is the identifier of a subnode of its attachment. It is 0 for an item that is not in an Outlook data file, such as the message of an .msg file.</summary>
    public uint NodeId => Source.NodeId;

    /// <summary>Gets whether the item is an embedded message, the content of an attachment.</summary>
    public bool IsEmbedded => EmbeddingDepth > 0;

    /// <summary>Gets whether the item is an associated (hidden) item of its folder.</summary>
    public bool IsAssociated => Source.IsAssociated;

    /// <summary>Gets the properties of the item.</summary>
    public MapiPropertyCollection Properties { get; }

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
    public MapiImportance Importance => (MapiImportance)(Properties.GetInt32(PropertyIds.Importance) ?? (int)MapiImportance.Normal);

    /// <summary>Gets the sensitivity of the item.</summary>
    public MapiSensitivity Sensitivity => (MapiSensitivity)(Properties.GetInt32(PropertyIds.Sensitivity) ?? 0);

    /// <summary>Gets the flags of the message, such as read or unsent.</summary>
    public MapiMessageFlags Flags => (MapiMessageFlags)(Properties.GetInt32(PropertyIds.MessageFlags) ?? (int)MapiMessageFlags.None);

    /// <summary>Gets whether the message has been read.</summary>
    public bool IsRead => (Flags & MapiMessageFlags.Read) != MapiMessageFlags.None;

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
    /// <exception cref="InvalidEmailDataException">The recipient table is malformed.</exception>
    public IReadOnlyList<MapiRecipient> Recipients => _recipients ??= LoadRecipients();

    /// <summary>Gets the attachments of the item.</summary>
    /// <exception cref="InvalidEmailDataException">The attachment table is malformed.</exception>
    public IReadOnlyList<MapiAttachment> Attachments => _attachments ??= LoadAttachments();

    /// <summary>Returns the RTF body, decompressed from the compressed RTF property.</summary>
    /// <returns>The RTF, or <see langword="null"/> when the message has no RTF body.</returns>
    /// <exception cref="InvalidEmailDataException">The compressed RTF is malformed.</exception>
    public byte[]? GetRtfBody()
    {
        var property = Properties.Find(PropertyIds.RtfCompressed);
        if (property is null || property.Type != MapiPropertyType.Binary)
        {
            return null;
        }

        return Lzfu.Decompress(property.GetBytes(), Source.Options.MaxPropertySize, Source.Options.VerifyChecksums);
    }

    /// <summary>Returns the HTML body: the HTML property, or the HTML encapsulated in the RTF body when the message has no HTML property, as Outlook often stores it ([MS-OXRTFEX]).</summary>
    /// <returns>The HTML, or <see langword="null"/> when the message has no HTML body.</returns>
    /// <exception cref="InvalidEmailDataException">The compressed RTF is malformed.</exception>
    public string? GetHtmlBody()
    {
        return BodyHtml ?? DecapsulateRtf(RtfEncapsulationFormat.Html);
    }

    /// <summary>Returns the plain text body: the body property, or the text encapsulated in the RTF body when the message has no body property ([MS-OXRTFEX]).</summary>
    /// <returns>The text, or <see langword="null"/> when the message has no plain text body.</returns>
    /// <exception cref="InvalidEmailDataException">The compressed RTF is malformed.</exception>
    public string? GetTextBody()
    {
        return Body ?? DecapsulateRtf(RtfEncapsulationFormat.Text);
    }

    /// <summary>Returns the subject.</summary>
    /// <returns>The subject.</returns>
    public override string ToString() => Subject ?? "";

    internal static void CheckDepth(PstReadOptions options, int embeddingDepth)
    {
        if (embeddingDepth > options.MaxEmbeddedMessageDepth)
        {
            throw new EmailResourceLimitException($"The embedded messages are nested deeper than the limit of {options.MaxEmbeddedMessageDepth} levels.");
        }
    }

    /// <summary>Creates the item, of the type its class gives.</summary>
    internal static MapiMessage Create(MessageSource source, MapiPropertyCollection properties, int embeddingDepth)
    {
        var messageClass = properties.GetString(PropertyIds.MessageClass) ?? "";
        if (IsClass(messageClass, "IPM.Contact"))
        {
            return new MapiContact(source, properties, embeddingDepth);
        }

        if (IsClass(messageClass, "IPM.DistList"))
        {
            return new MapiDistributionList(source, properties, embeddingDepth);
        }

        if (IsClass(messageClass, "IPM.Appointment"))
        {
            return new MapiAppointment(source, properties, embeddingDepth);
        }

        if (IsClass(messageClass, "IPM.Task"))
        {
            return new MapiTask(source, properties, embeddingDepth);
        }

        return new MapiMessage(source, properties, embeddingDepth);
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

    private string? DecapsulateRtf(RtfEncapsulationFormat format)
    {
        if (!HasRtfBody || GetRtfBody() is not { } rtf || RtfEncapsulation.GetFormat(rtf) != format)
        {
            return null;
        }

        return RtfEncapsulation.Decapsulate(rtf);
    }

    private List<MapiRecipient> LoadRecipients() => Source.LoadRecipients(this);

    private List<MapiAttachment> LoadAttachments() => Source.LoadAttachments(this);
}
