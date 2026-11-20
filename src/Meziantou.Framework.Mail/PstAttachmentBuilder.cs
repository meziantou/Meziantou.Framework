using Meziantou.Framework.Mail.Internals;

namespace Meziantou.Framework.Mail;

/// <summary>An attachment of an item to write: a file, or an embedded message.</summary>
public sealed class PstAttachmentBuilder
{
    private PstMessageBuilder? _embeddedMessage;

    /// <summary>Gets or sets whether the builder copies an attachment read from a file: the writer then writes its properties as they are.</summary>
    internal bool IsCopy { get; set; }

    /// <summary>Gets the properties of the attachment. The other members of the builder read and write them.</summary>
    public PstPropertyBag Properties { get; } = new();

    /// <summary>Gets or sets the file name of the attachment. The writer also uses it as the long file name and the display name when they are not set.</summary>
    public string? FileName
    {
        get => Properties.GetValue<string>(PropertyIds.AttachFilename);
        set => Properties.SetValue(PropertyIds.AttachFilename, PstPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the full file name of the attachment.</summary>
    public string? LongFileName
    {
        get => Properties.GetValue<string>(PropertyIds.AttachLongFilename);
        set => Properties.SetValue(PropertyIds.AttachLongFilename, PstPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the display name of the attachment.</summary>
    public string? DisplayName
    {
        get => Properties.GetValue<string>(PropertyIds.DisplayName);
        set => Properties.SetValue(PropertyIds.DisplayName, PstPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the MIME type of the content.</summary>
    public string? MimeType
    {
        get => Properties.GetValue<string>(PropertyIds.AttachMimeTag);
        set => Properties.SetValue(PropertyIds.AttachMimeTag, PstPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets the content identifier, which an HTML body uses to show the attachment inline (<c>cid:</c> URLs).</summary>
    public string? ContentId
    {
        get => Properties.GetValue<string>(PropertyIds.AttachContentId);
        set => Properties.SetValue(PropertyIds.AttachContentId, PstPropertyType.Unicode, value);
    }

    /// <summary>Gets or sets whether the attachment is hidden, such as an image shown inline.</summary>
    public bool IsHidden
    {
        get => Properties.Get(PropertyIds.AttachmentHidden) is true;
        set => Properties.SetValue(PropertyIds.AttachmentHidden, PstPropertyType.Boolean, value ? true : null);
    }

    /// <summary>Sets the content of the attachment.</summary>
    /// <param name="content">The content.</param>
    public void SetContent(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        SetContentValue(content);
    }

    /// <summary>Sets a stream the writer reads the content of the attachment from, block by block, when it writes the item. The writer does not dispose it.</summary>
    /// <param name="content">The stream of the content, read from its current position.</param>
    public void SetContent(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        SetContentValue(content);
    }

    /// <summary>Gets or sets the embedded message, for an attachment that is a message.</summary>
    public PstMessageBuilder? EmbeddedMessage
    {
        get => _embeddedMessage;
        set
        {
            _embeddedMessage = value;
            if (value is not null)
            {
                Properties.Remove(PropertyIds.AttachDataBinary);
                Properties.SetValue(PropertyIds.AttachMethod, PstPropertyType.Integer32, (int)PstAttachmentMethod.EmbeddedMessage);
            }
        }
    }

    /// <summary>Creates an attachment from the content of a file.</summary>
    /// <param name="fileName">The file name of the attachment.</param>
    /// <param name="content">The content.</param>
    /// <param name="mimeType">The MIME type of the content, or <see langword="null"/>.</param>
    /// <returns>The attachment.</returns>
    public static PstAttachmentBuilder FromContent(string fileName, byte[] content, string? mimeType = null)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(content);
        var attachment = new PstAttachmentBuilder { FileName = fileName, MimeType = mimeType };
        attachment.SetContent(content);
        return attachment;
    }

    /// <summary>Creates an attachment that contains a message.</summary>
    /// <param name="message">The embedded message.</param>
    /// <returns>The attachment.</returns>
    public static PstAttachmentBuilder FromMessage(PstMessageBuilder message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new PstAttachmentBuilder { EmbeddedMessage = message, DisplayName = message.Subject };
    }

    private void SetContentValue(object content)
    {
        _embeddedMessage = null;
        Properties.SetValue(PropertyIds.AttachDataBinary, PstPropertyType.Binary, content);
        Properties.SetValue(PropertyIds.AttachMethod, PstPropertyType.Integer32, (int)PstAttachmentMethod.ByValue);
    }
}
