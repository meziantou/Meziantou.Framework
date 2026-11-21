using Meziantou.Framework.Mail.Internals;

namespace Meziantou.Framework.Mail;

/// <summary>An attachment of an item: a file, or an embedded message.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Mail.Tests/PstFileTests.cs" ref:region="PstAttachment_Save" ref:dedent="true">
/// using var file = PstFile.Open(path);
/// var message = file.GetFolder("Inbox")!.EnumerateItems().First();
/// foreach (var attachment in message.Attachments)
/// {
///     if (attachment.Method == PstAttachmentMethod.ByValue)
///     {
///         // Read the content block by block, without loading it in memory
///         using var content = attachment.OpenRead();
///         using var output = File.Create(Path.Combine(directory.FullName, Path.GetFileName(attachment.Name ?? "attachment")));
///         content.CopyTo(output);
///     }
/// }
/// </code>
/// </example>
public sealed class PstAttachment
{
    private readonly PstMessage _message;
    private readonly AttachmentSource _source;

    internal PstAttachment(PstMessage message, AttachmentSource source)
    {
        _message = message;
        _source = source;
        Properties = source.Properties;
    }

    /// <summary>Gets the message the attachment belongs to.</summary>
    public PstMessage Message => _message;

    /// <summary>Gets the properties of the attachment.</summary>
    public PstPropertyCollection Properties { get; }

    /// <summary>Gets the short (8.3) file name of the attachment.</summary>
    public string? FileName => Properties.GetString(PropertyIds.AttachFilename);

    /// <summary>Gets the full file name of the attachment.</summary>
    public string? LongFileName => Properties.GetString(PropertyIds.AttachLongFilename);

    /// <summary>Gets the display name of the attachment, the name Outlook shows (for an embedded message, its subject).</summary>
    public string? DisplayName => Properties.GetString(PropertyIds.DisplayName);

    /// <summary>Gets the best available name: the long file name, the short file name, or the display name.</summary>
    public string? Name => LongFileName ?? FileName ?? DisplayName;

    /// <summary>Gets the extension of the file name, such as <c>.pdf</c>.</summary>
    public string? Extension => Properties.GetString(PropertyIds.AttachExtension);

    /// <summary>Gets the MIME type of the content, such as <c>image/png</c>.</summary>
    public string? MimeType => Properties.GetString(PropertyIds.AttachMimeTag);

    /// <summary>Gets the content identifier, which the HTML body uses to show the attachment inline (<c>cid:</c> URLs).</summary>
    public string? ContentId => Properties.GetString(PropertyIds.AttachContentId);

    /// <summary>Gets the size of the attachment, as stored in the file (it includes the size of the properties).</summary>
    public int? Size => Properties.GetInt32(PropertyIds.AttachSize);

    /// <summary>Gets how the content of the attachment is stored.</summary>
    public PstAttachmentMethod Method => (PstAttachmentMethod)(Properties.GetInt32(PropertyIds.AttachMethod) ?? 0);

    /// <summary>Gets the position of the attachment in the rich text body, or -1.</summary>
    public int RenderingPosition => Properties.GetInt32(PropertyIds.RenderingPosition) ?? -1;

    /// <summary>Gets whether the attachment is hidden, such as an image shown inline in the HTML body.</summary>
    public bool IsHidden => Properties.GetBoolean(PropertyIds.AttachmentHidden) ?? false;

    /// <summary>Gets whether the attachment is an embedded message.</summary>
    public bool IsEmbeddedMessage => Properties.Find(new PstPropertyTag(PropertyIds.AttachDataBinary, PstPropertyType.ObjectReference)) is not null || Method == PstAttachmentMethod.EmbeddedMessage;

    /// <summary>Opens a stream on the content of the attachment. Large contents are read block by block.</summary>
    /// <returns>A read-only stream; an empty stream when the attachment has no binary content (such as an embedded message).</returns>
    /// <exception cref="InvalidPstException">The content is malformed.</exception>
    public Stream OpenRead()
    {
        var property = Properties.Find(new PstPropertyTag(PropertyIds.AttachDataBinary, PstPropertyType.Binary));
        return property is null ? new MemoryStream([], writable: false) : property.OpenRead();
    }

    /// <summary>Returns the content of the attachment.</summary>
    /// <returns>The content; an empty array when the attachment has no binary content.</returns>
    /// <exception cref="InvalidPstException">The content is malformed.</exception>
    /// <exception cref="PstResourceLimitException">The content is larger than <see cref="PstReadOptions.MaxPropertySize"/>.</exception>
    public byte[] GetContent()
    {
        var property = Properties.Find(new PstPropertyTag(PropertyIds.AttachDataBinary, PstPropertyType.Binary));
        return property is null ? [] : property.GetBytes();
    }

    /// <summary>Returns the embedded message of an attachment whose method is <see cref="PstAttachmentMethod.EmbeddedMessage"/>.</summary>
    /// <returns>The embedded message, or <see langword="null"/> when the attachment is not an embedded message.</returns>
    /// <exception cref="InvalidPstException">The embedded message is malformed.</exception>
    /// <exception cref="PstResourceLimitException">The embedded messages are nested deeper than <see cref="PstReadOptions.MaxEmbeddedMessageDepth"/>.</exception>
    public PstMessage? GetEmbeddedMessage()
    {
        var property = Properties.Find(new PstPropertyTag(PropertyIds.AttachDataBinary, PstPropertyType.ObjectReference));
        if (property is null)
        {
            return null;
        }

        return _source.LoadEmbeddedMessage(property, _message.EmbeddingDepth + 1);
    }

    /// <summary>Returns the name of the attachment.</summary>
    /// <returns>The name of the attachment.</returns>
    public override string ToString() => Name ?? "";
}
