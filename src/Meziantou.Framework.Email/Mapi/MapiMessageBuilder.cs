using Meziantou.Framework.Email.Internals;

namespace Meziantou.Framework.Email.Mapi;

/// <summary>An item to add to a folder of a <see cref="PstWriter"/>: an email, or the base of the typed items (contacts, appointments, tasks, distribution lists).</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Email.Tests/PstWriterTests.cs" ref:region="PstWriter_Create" ref:dedent="true">
/// using (var writer = PstWriter.Create(path, new PstWriterOptions { DisplayName = "Archive" }))
/// {
///     var inbox = writer.RootFolder.CreateFolder("Inbox", "IPF.Note");
///     var message = new MapiMessageBuilder
///     {
///         Subject = "Hello",
///         Body = "Hello from Meziantou.Framework.Email",
///         SenderName = "Alice",
///         SenderEmailAddress = "alice@example.com",
///         SenderAddressType = "SMTP",
///         ClientSubmitTime = new DateTime(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc),
///     };
///     message.Recipients.Add(new MapiRecipientBuilder("Bob", "bob@example.com"));
///     message.Attachments.Add(MapiAttachmentBuilder.FromContent("notes.txt", "Some notes"u8.ToArray(), "text/plain"));
///     inbox.AddItem(message);
///
///     var contacts = writer.RootFolder.CreateFolder("Contacts", "IPF.Contact");
///     contacts.AddItem(new MapiContactBuilder { GivenName = "Bob", Surname = "Smith", Email1Address = "bob@example.com" });
/// }
///
/// using var file = PstFile.Open(path);
/// var item = file.GetFolder("Inbox")!.EnumerateItems().Single();
/// Assert.Equal("Hello", item.Subject);
/// Assert.Equal("bob@example.com", item.Recipients.Single().EmailAddress);
/// Assert.Equal("notes.txt", item.Attachments.Single().Name);
/// </code>
/// </example>
public class MapiMessageBuilder
{
    /// <summary>Initializes a new instance of the <see cref="MapiMessageBuilder"/> class for an email (IPM.Note).</summary>
    public MapiMessageBuilder()
        : this("IPM.Note")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MapiMessageBuilder"/> class for an item of a class.</summary>
    /// <param name="messageClass">The class of the item, such as <c>IPM.Note</c> or <c>IPM.StickyNote</c>.</param>
    public MapiMessageBuilder(string messageClass)
    {
        ArgumentNullException.ThrowIfNull(messageClass);
        MessageClass = messageClass;
    }

    /// <summary>Gets the properties of the item. The other members of the builder read and write them.</summary>
    public MapiPropertyBag Properties { get; } = new();

    /// <summary>Gets the recipients of the message.</summary>
    public IList<MapiRecipientBuilder> Recipients { get; } = [];

    /// <summary>Gets the attachments of the item.</summary>
    public IList<MapiAttachmentBuilder> Attachments { get; } = [];

    /// <summary>Gets or sets whether the builder copies an item read from a file: the writer then writes its properties as they are, without the defaults of the typed builders.</summary>
    internal bool IsCopy { get; set; }

    /// <summary>Gets or sets whether the item is an associated (hidden) item of its folder, such as a view or a setting.</summary>
    public bool IsAssociated { get; set; }

    /// <summary>Gets or sets the class of the item.</summary>
    public string MessageClass
    {
        get => Properties.GetValue<string>(PropertyIds.MessageClass) ?? "IPM.Note";
        set => Properties.SetValue(PropertyIds.MessageClass, MapiPropertyType.Unicode, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>Gets or sets the subject.</summary>
    public string? Subject
    {
        get => Properties.GetValue<string>(PropertyIds.Subject);
        set => Properties.SetValue(PropertyIds.Subject, MapiPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the topic of the conversation, the subject without its prefix.</summary>
    public string? ConversationTopic
    {
        get => Properties.GetValue<string>(PropertyIds.ConversationTopic);
        set => Properties.SetValue(PropertyIds.ConversationTopic, MapiPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the plain text body.</summary>
    public string? Body
    {
        get => Properties.GetValue<string>(PropertyIds.Body);
        set => Properties.SetValue(PropertyIds.Body, MapiPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the HTML body, written in UTF-8.</summary>
    public string? BodyHtml
    {
        get => Properties.Get(PropertyIds.Html) is byte[] bytes ? System.Text.Encoding.UTF8.GetString(bytes) : null;
        set
        {
            Properties.SetValue(PropertyIds.Html, MapiPropertyType.Binary, value is null ? null : System.Text.Encoding.UTF8.GetBytes(value));
            Properties.SetValue(PropertyIds.InternetCodepage, MapiPropertyType.Integer32, value is null ? null : 65001);
        }
    }

    /// <summary>Gets or sets the display name of the sender.</summary>
    public string? SenderName
    {
        get => Properties.GetValue<string>(PropertyIds.SenderName);
        set => Properties.SetValue(PropertyIds.SenderName, MapiPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the address of the sender.</summary>
    public string? SenderEmailAddress
    {
        get => Properties.GetValue<string>(PropertyIds.SenderEmailAddress);
        set => Properties.SetValue(PropertyIds.SenderEmailAddress, MapiPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the type of the address of the sender, such as <c>SMTP</c>.</summary>
    public string? SenderAddressType
    {
        get => Properties.GetValue<string>(PropertyIds.SenderAddressType);
        set => Properties.SetValue(PropertyIds.SenderAddressType, MapiPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the display name of the user the message is sent on behalf of.</summary>
    public string? SentRepresentingName
    {
        get => Properties.GetValue<string>(PropertyIds.SentRepresentingName);
        set => Properties.SetValue(PropertyIds.SentRepresentingName, MapiPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the address of the user the message is sent on behalf of.</summary>
    public string? SentRepresentingEmailAddress
    {
        get => Properties.GetValue<string>(PropertyIds.SentRepresentingEmailAddress);
        set => Properties.SetValue(PropertyIds.SentRepresentingEmailAddress, MapiPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the date and time when the message was sent.</summary>
    public DateTime? ClientSubmitTime
    {
        get => Properties.Get(PropertyIds.ClientSubmitTime) as DateTime?;
        set => Properties.SetValue(PropertyIds.ClientSubmitTime, MapiPropertyType.Time, value);
    }

    /// <summary>Gets or sets the date and time when the message was received.</summary>
    public DateTime? MessageDeliveryTime
    {
        get => Properties.Get(PropertyIds.MessageDeliveryTime) as DateTime?;
        set => Properties.SetValue(PropertyIds.MessageDeliveryTime, MapiPropertyType.Time, value);
    }

    /// <summary>Gets or sets the date and time when the item was created. The writer uses the current date when it is not set.</summary>
    public DateTime? CreationTime
    {
        get => Properties.Get(PropertyIds.CreationTime) as DateTime?;
        set => Properties.SetValue(PropertyIds.CreationTime, MapiPropertyType.Time, value);
    }

    /// <summary>Gets or sets the date and time when the item was last modified. The writer uses the creation date when it is not set.</summary>
    public DateTime? LastModificationTime
    {
        get => Properties.Get(PropertyIds.LastModificationTime) as DateTime?;
        set => Properties.SetValue(PropertyIds.LastModificationTime, MapiPropertyType.Time, value);
    }

    /// <summary>Gets or sets the importance.</summary>
    public MapiImportance Importance
    {
        get => Properties.Get(PropertyIds.Importance) is int value ? (MapiImportance)value : MapiImportance.Normal;
        set => Properties.SetValue(PropertyIds.Importance, MapiPropertyType.Integer32, (int)value);
    }

    /// <summary>Gets or sets the sensitivity.</summary>
    public MapiSensitivity Sensitivity
    {
        get => Properties.Get(PropertyIds.Sensitivity) is int value ? (MapiSensitivity)value : MapiSensitivity.Normal;
        set => Properties.SetValue(PropertyIds.Sensitivity, MapiPropertyType.Integer32, (int)value);
    }

    /// <summary>Gets or sets the flags of the message. The writer adds <see cref="MapiMessageFlags.HasAttachments"/> and <see cref="MapiMessageFlags.Associated"/> when they apply.</summary>
    public MapiMessageFlags Flags
    {
        get => Properties.Get(PropertyIds.MessageFlags) is int value ? (MapiMessageFlags)value : MapiMessageFlags.Read;
        set => Properties.SetValue(PropertyIds.MessageFlags, MapiPropertyType.Integer32, (int)value);
    }

    /// <summary>Gets or sets the Message-ID header.</summary>
    public string? InternetMessageId
    {
        get => Properties.GetValue<string>(PropertyIds.InternetMessageId);
        set => Properties.SetValue(PropertyIds.InternetMessageId, MapiPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the In-Reply-To header.</summary>
    public string? InReplyToId
    {
        get => Properties.GetValue<string>(PropertyIds.InReplyToId);
        set => Properties.SetValue(PropertyIds.InReplyToId, MapiPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the References header.</summary>
    public string? InternetReferences
    {
        get => Properties.GetValue<string>(PropertyIds.InternetReferences);
        set => Properties.SetValue(PropertyIds.InternetReferences, MapiPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the headers of the message, as received.</summary>
    public string? TransportMessageHeaders
    {
        get => Properties.GetValue<string>(PropertyIds.TransportMessageHeaders);
        set => Properties.SetValue(PropertyIds.TransportMessageHeaders, MapiPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the categories.</summary>
    public IReadOnlyList<string>? Categories
    {
        get => Properties.GetValue<string[]>(NamedProperties.Keywords);
        set => Properties.SetValue(NamedProperties.Keywords, value?.ToArray());
    }

    /// <summary>Sets the RTF body: compresses it to the compressed RTF property ([MS-OXRTFCP]).</summary>
    /// <param name="rtf">The RTF, or <see langword="null"/> to remove the RTF body.</param>
    public void SetRtfBody(ReadOnlySpan<byte> rtf)
    {
        Properties.SetValue(PropertyIds.RtfCompressed, MapiPropertyType.Binary, Lzfu.Compress(rtf));
        Properties.SetValue(PropertyIds.RtfInSync, MapiPropertyType.Boolean, true);
    }

    /// <summary>Creates a builder that copies an item read from a file: its properties, recipients and attachments, including embedded messages.</summary>
    /// <param name="message">The item to copy.</param>
    /// <returns>The builder of the matching type (<see cref="MapiContactBuilder"/> for a <see cref="MapiContact"/>...).</returns>
    /// <exception cref="InvalidEmailDataException">The item is malformed.</exception>
    public static MapiMessageBuilder FromMessage(MapiMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        MapiMessageBuilder builder = message switch
        {
            MapiContact => new MapiContactBuilder(),
            MapiDistributionList => new MapiDistributionListBuilder(),
            MapiAppointment => new MapiAppointmentBuilder(),
            MapiTask => new MapiTaskBuilder(),
            _ => new MapiMessageBuilder(),
        };

        builder.IsAssociated = message.IsAssociated;
        builder.IsCopy = true;
        PstCopier.CopyProperties(message.Properties, builder.Properties, PstCopier.ComputedMessageProperties);
        if (message.Properties.Find(PropertyIds.MessageClass) is null)
        {
            builder.Properties.Remove(PropertyIds.MessageClass);
        }
        foreach (var recipient in message.Recipients)
        {
            var copy = new MapiRecipientBuilder();
            PstCopier.CopyProperties(recipient.Properties, copy.Properties, PstCopier.ComputedRecipientProperties);
            builder.Recipients.Add(copy);
        }

        foreach (var attachment in message.Attachments)
        {
            var copy = new MapiAttachmentBuilder { IsCopy = true };
            if (attachment.GetEmbeddedMessage() is { } embedded)
            {
                copy.EmbeddedMessage = FromMessage(embedded);
            }

            PstCopier.CopyProperties(attachment.Properties, copy.Properties, PstCopier.ComputedAttachmentProperties);
            if (attachment.Properties.Find(PropertyIds.AttachMethod) is null)
            {
                copy.Properties.Remove(PropertyIds.AttachMethod);
            }

            builder.Attachments.Add(copy);
        }

        return builder;
    }
}
