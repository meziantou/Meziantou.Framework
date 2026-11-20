using System.Text;
using Meziantou.Framework.Mail.Internals;

namespace Meziantou.Framework.Mail;

/// <summary>A leaf MIME part: a text body or an attachment.</summary>
public sealed class MimePart : MimeEntity
{
    private ReadOnlyMemory<byte> _rawContent;
    private byte[]? _content;

    /// <summary>Initializes a new instance of the <see cref="MimePart"/> class with no content.</summary>
    public MimePart()
    {
    }

    internal MimePart(ReadOnlyMemory<byte> rawContent)
    {
        _rawContent = rawContent;
    }

    /// <summary>Gets or sets the transfer encoding of the content, such as <c>base64</c>, <c>quoted-printable</c> or <c>7bit</c>.</summary>
    public string? ContentTransferEncoding
    {
        get => Headers["Content-Transfer-Encoding"]?.Trim().ToLowerInvariant();
        set
        {
            // The content must be encoded again with the new encoding
            _content ??= GetContent();
            Headers.Set("Content-Transfer-Encoding", value is null ? null : MimeHeader.CreateRaw("Content-Transfer-Encoding", value));
        }
    }

    /// <summary>Gets the file name: the filename parameter of the disposition, or the name parameter of the content type.</summary>
    public string? FileName => ContentDisposition?.FileName ?? ContentType.Name;

    /// <summary>Gets whether the part is an attachment: its disposition is <c>attachment</c>.</summary>
    public bool IsAttachment => ContentDisposition?.IsAttachment == true;

    /// <summary>Returns the decoded content of the part.</summary>
    /// <returns>The content.</returns>
    public byte[] GetContent()
    {
        if (_content is not null)
        {
            return _content;
        }

        return ContentTransferEncoding switch
        {
            "base64" => MimeEncoding.DecodeBase64(_rawContent.Span),
            "quoted-printable" => MimeEncoding.DecodeQuotedPrintable(_rawContent.Span),
            "x-uuencode" or "uuencode" or "x-uue" => MimeUuencode.Decode(_rawContent.Span),
            _ => _rawContent.ToArray(),
        };
    }

    /// <summary>Returns the content decoded as text with the charset of the part (UTF-8 or Latin-1 when it has none).</summary>
    /// <returns>The text.</returns>
    public string GetText() => MimeEncoding.DecodeText(GetContent(), ContentType.Charset);

    /// <summary>Sets the content. The writer encodes it with the transfer encoding, which it chooses when the part has none.</summary>
    /// <param name="content">The content.</param>
    public void SetContent(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
        _rawContent = default;
    }

    /// <summary>Sets a text content, in UTF-8.</summary>
    /// <param name="text">The text.</param>
    /// <param name="mediaSubtype">The subtype of <c>text</c>, such as <c>plain</c> or <c>html</c>.</param>
    public void SetText(string text, string mediaSubtype = "plain")
    {
        ArgumentNullException.ThrowIfNull(text);
        var contentType = ContentType.IsMimeType("text", mediaSubtype) ? ContentType : new MimeContentType("text", mediaSubtype);
        contentType.Charset = "utf-8";
        ContentType = contentType;
        SetContent(Encoding.UTF8.GetBytes(text));
    }

    internal override void WriteTo(Stream stream)
    {
        if (_content is null)
        {
            MimeWriter.WriteHeaders(stream, Headers);
            stream.Write(_rawContent.Span);
            return;
        }

        var encoding = ContentTransferEncoding;
        if ((encoding is null or "7bit" or "8bit" or "binary" && !IsSafe(_content, encoding)) || encoding is not (null or "7bit" or "8bit" or "binary" or "base64" or "quoted-printable"))
        {
            // The content cannot be written as it is, or with its encoding (uuencode): choose an encoding
            encoding = ContentType.MediaType.Equals("text", StringComparison.OrdinalIgnoreCase) ? "quoted-printable" : "base64";
            Headers.Set("Content-Transfer-Encoding", MimeHeader.CreateRaw("Content-Transfer-Encoding", encoding));
        }

        MimeWriter.WriteHeaders(stream, Headers);
        stream.Write(encoding switch
        {
            "base64" => MimeEncoding.EncodeBase64(_content),
            "quoted-printable" => MimeEncoding.EncodeQuotedPrintable(_content),
            _ => _content,
        });
    }

    /// <summary>Returns whether a content can be written without encoding: short lines, and ASCII for 7bit.</summary>
    private static bool IsSafe(ReadOnlySpan<byte> content, string? encoding)
    {
        if (encoding is "binary")
        {
            return true;
        }

        var lineLength = 0;
        for (var i = 0; i < content.Length; i++)
        {
            var b = content[i];
            if (b == '\n')
            {
                if (i == 0 || content[i - 1] != '\r')
                {
                    return false;
                }

                lineLength = 0;
                continue;
            }

            if (b == 0 || (b == '\r' && (i + 1 >= content.Length || content[i + 1] != '\n')) || (b > 127 && encoding is not "8bit") || ++lineLength > 998)
            {
                return false;
            }
        }

        return true;
    }
}
