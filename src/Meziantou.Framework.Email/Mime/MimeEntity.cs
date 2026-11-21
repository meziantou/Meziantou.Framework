namespace Meziantou.Framework.Email;

/// <summary>A MIME entity: a leaf part (<see cref="MimePart"/>), a multipart (<see cref="MimeMultipart"/>) or an embedded message (<see cref="MimeMessagePart"/>).</summary>
public abstract class MimeEntity
{
    private protected MimeEntity()
    {
    }

    /// <summary>Gets the headers of the entity: Content-Type, Content-Disposition, Content-Transfer-Encoding...</summary>
    public MimeHeaderCollection Headers { get; } = new();

    /// <summary>Gets or sets the content type. The default is <c>text/plain</c> ([RFC 2045] 5.2).</summary>
    public MimeContentType ContentType
    {
        get => Headers.Find("Content-Type") is { } header ? MimeContentType.Parse(header.RawValue) : DefaultContentType;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Headers.Set("Content-Type", MimeHeader.CreateRaw("Content-Type", value.ToString()));
        }
    }

    /// <summary>Gets or sets the content disposition, or <see langword="null"/> when the entity has none.</summary>
    public MimeContentDisposition? ContentDisposition
    {
        get => Headers.Find("Content-Disposition") is { } header ? MimeContentDisposition.Parse(header.RawValue) : null;
        set => Headers.Set("Content-Disposition", value is null ? null : MimeHeader.CreateRaw("Content-Disposition", value.ToString()));
    }

    /// <summary>Gets or sets the identifier of the content, without its angle brackets, which an HTML body references with <c>cid:</c> URLs.</summary>
    public string? ContentId
    {
        get => Headers["Content-ID"]?.Trim().TrimStart('<').TrimEnd('>');
        set => Headers.Set("Content-ID", value is null ? null : MimeHeader.CreateRaw("Content-ID", "<" + value + ">"));
    }

    /// <summary>Gets the content type an entity has when it has no Content-Type header: <c>text/plain</c>, or <c>message/rfc822</c> in a <c>multipart/digest</c>.</summary>
    internal MimeContentType DefaultContentType { get; set; } = new("text", "plain");

    /// <summary>Gets or sets the bytes the entity was read from, with its headers, when it is a part of a multipart or an entity read alone: S/MIME checks a signature on them, as writing the entity can change white space the signature covers.</summary>
    internal ReadOnlyMemory<byte>? RawData { get; set; }

    /// <summary>Writes the entity: its headers and its content.</summary>
    internal abstract void WriteTo(Stream stream);
}
