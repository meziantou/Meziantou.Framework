using System.Text;
using Meziantou.Framework.Mail.Internals;

namespace Meziantou.Framework.Mail;

/// <summary>The value of a Content-Type header ([RFC 2045] 5): a media type, a subtype and parameters.</summary>
public sealed class MimeContentType
{
    /// <summary>Initializes a new instance of the <see cref="MimeContentType"/> class.</summary>
    /// <param name="mediaType">The media type, such as <c>text</c>.</param>
    /// <param name="mediaSubtype">The media subtype, such as <c>plain</c>.</param>
    public MimeContentType(string mediaType, string mediaSubtype)
    {
        ArgumentNullException.ThrowIfNull(mediaType);
        ArgumentNullException.ThrowIfNull(mediaSubtype);
        MediaType = mediaType;
        MediaSubtype = mediaSubtype;
    }

    /// <summary>Gets or sets the media type, such as <c>text</c> or <c>multipart</c>.</summary>
    public string MediaType { get; set; }

    /// <summary>Gets or sets the media subtype, such as <c>plain</c> or <c>mixed</c>.</summary>
    public string MediaSubtype { get; set; }

    /// <summary>Gets the type and the subtype, such as <c>text/plain</c>.</summary>
    public string MimeType => MediaType + "/" + MediaSubtype;

    /// <summary>Gets the parameters, with case-insensitive names.</summary>
    public IDictionary<string, string> Parameters { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the charset parameter.</summary>
    public string? Charset
    {
        get => Parameters.TryGetValue("charset", out var value) ? value : null;
        set => SetParameter("charset", value);
    }

    /// <summary>Gets or sets the boundary parameter of a multipart type.</summary>
    public string? Boundary
    {
        get => Parameters.TryGetValue("boundary", out var value) ? value : null;
        set => SetParameter("boundary", value);
    }

    /// <summary>Gets or sets the name parameter, the file name that old clients give to an attachment.</summary>
    public string? Name
    {
        get => Parameters.TryGetValue("name", out var value) ? value : null;
        set => SetParameter("name", value);
    }

    /// <summary>Returns whether the type is the given type and subtype, ignoring case.</summary>
    /// <param name="mediaType">The media type, or <c>*</c> for any type.</param>
    /// <param name="mediaSubtype">The media subtype, or <c>*</c> for any subtype.</param>
    /// <returns><see langword="true"/> when the type matches.</returns>
    public bool IsMimeType(string mediaType, string mediaSubtype)
    {
        return (mediaType == "*" || MediaType.Equals(mediaType, StringComparison.OrdinalIgnoreCase))
            && (mediaSubtype == "*" || MediaSubtype.Equals(mediaSubtype, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Parses the value of a Content-Type header. An invalid value gives <c>text/plain</c> ([RFC 2045] 5.2).</summary>
    /// <param name="value">The value of the header, with its encoded words.</param>
    /// <returns>The content type.</returns>
    public static MimeContentType Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var tokenizer = new MimeTokenizer(value);
        var type = tokenizer.ReadToken("/;");
        var result = new MimeContentType("text", "plain");
        if (tokenizer.TryConsume('/'))
        {
            var subtype = tokenizer.ReadToken(";");
            if (type.Length > 0 && subtype.Length > 0)
            {
                result.MediaType = type;
                result.MediaSubtype = subtype;
            }
        }
        else if (type.Equals("text", StringComparison.OrdinalIgnoreCase))
        {
            // "Content-Type: text" is a common mistake for text/plain
            result.MediaType = "text";
        }

        foreach (var (name, parameter) in MimeParameters.Parse(ref tokenizer))
        {
            result.Parameters[name] = parameter;
        }

        return result;
    }

    /// <summary>Returns the value of the header: the type, the subtype and the parameters.</summary>
    /// <returns>The value of the header.</returns>
    public override string ToString()
    {
        var builder = new StringBuilder(MimeType);
        MimeParameters.Format(builder, Parameters);
        return builder.ToString();
    }

    private void SetParameter(string name, string? value)
    {
        if (value is null)
        {
            Parameters.Remove(name);
        }
        else
        {
            Parameters[name] = value;
        }
    }
}
