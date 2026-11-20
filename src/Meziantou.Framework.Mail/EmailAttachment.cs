namespace Meziantou.Framework.Mail;

/// <summary>An attachment of an <see cref="EmailMessage"/>: a file, or an embedded message.</summary>
public sealed class EmailAttachment
{
    /// <summary>Gets or sets the file name.</summary>
    public string? FileName { get; set; }

    /// <summary>Gets or sets the MIME type of the content, such as <c>image/png</c>.</summary>
    public string ContentType { get; set; } = "application/octet-stream";

    /// <summary>Gets or sets the identifier of the content, without angle brackets, which an HTML body references with <c>cid:</c> URLs.</summary>
    public string? ContentId { get; set; }

    /// <summary>Gets or sets whether the attachment is shown in the body, such as an image of an HTML body, rather than listed as a file.</summary>
    public bool IsInline { get; set; }

    /// <summary>Gets or sets the content of a file attachment.</summary>
    public ReadOnlyMemory<byte> Content { get; set; }

    /// <summary>Gets or sets the message of an attachment that is a message, or <see langword="null"/> for a file.</summary>
    public EmailMessage? EmbeddedMessage { get; set; }

    /// <summary>Returns the file name.</summary>
    /// <returns>The file name.</returns>
    public override string ToString() => FileName ?? "";
}
