using System.Security.Cryptography;

namespace Meziantou.Framework.Mail;

/// <summary>A multipart entity ([RFC 2046] 5.1): parts separated by a boundary, such as <c>multipart/mixed</c> or <c>multipart/alternative</c>.</summary>
public sealed class MimeMultipart : MimeEntity
{
    /// <summary>Initializes a new instance of the <see cref="MimeMultipart"/> class.</summary>
    /// <param name="mediaSubtype">The subtype, such as <c>mixed</c>, <c>alternative</c> or <c>related</c>.</param>
    public MimeMultipart(string mediaSubtype = "mixed")
    {
        ArgumentNullException.ThrowIfNull(mediaSubtype);
        ContentType = new MimeContentType("multipart", mediaSubtype);
    }

    internal MimeMultipart(bool parsed)
    {
        _ = parsed;
    }

    /// <summary>Gets the parts.</summary>
    public IList<MimeEntity> Parts { get; } = [];

    /// <summary>Gets or sets the text before the first part, which MIME clients do not show.</summary>
    public ReadOnlyMemory<byte> Preamble { get; set; }

    /// <summary>Gets or sets the text after the last part.</summary>
    public ReadOnlyMemory<byte> Epilogue { get; set; }

    /// <summary>Gets or sets whether the multipart ends with a close delimiter; a multipart read from a message without one is written without one.</summary>
    internal bool IsClosed { get; set; } = true;

    internal override void WriteTo(Stream stream)
    {
        var contentType = ContentType;
        var boundary = contentType.Boundary;
        if (Parts.Count == 0 && (string.IsNullOrEmpty(boundary) || !IsClosed))
        {
            // A multipart read without a boundary or without a delimiter: its content is its preamble
            Internals.MimeWriter.WriteHeaders(stream, Headers);
            stream.Write(Preamble.Span);
            return;
        }

        if (string.IsNullOrEmpty(boundary))
        {
            boundary = "=_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
            contentType.Boundary = boundary;
            ContentType = contentType;
        }

        Internals.MimeWriter.WriteHeaders(stream, Headers);
        // The line break before a delimiter belongs to the delimiter ([RFC 2046] 5.1.1)
        if (!Preamble.IsEmpty)
        {
            stream.Write(Preamble.Span);
            stream.Write("\r\n"u8);
        }

        var delimiter = System.Text.Encoding.ASCII.GetBytes("--" + boundary);
        for (var i = 0; i < Parts.Count; i++)
        {
            stream.Write(delimiter);
            stream.Write("\r\n"u8);
            Parts[i].WriteTo(stream);
            if (IsClosed || i < Parts.Count - 1)
            {
                stream.Write("\r\n"u8);
            }
        }

        if (!IsClosed)
        {
            return;
        }

        // The line break after the close delimiter is the one before the delimiter of the enclosing multipart, or the end of the message
        stream.Write(delimiter);
        stream.Write("--"u8);
        if (!Epilogue.IsEmpty)
        {
            stream.Write("\r\n"u8);
            stream.Write(Epilogue.Span);
        }
    }
}
