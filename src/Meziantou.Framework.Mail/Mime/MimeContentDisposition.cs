using System.Text;
using Meziantou.Framework.Mail.Internals;

namespace Meziantou.Framework.Mail;

/// <summary>The value of a Content-Disposition header ([RFC 2183]): <c>inline</c> or <c>attachment</c>, and parameters such as the file name.</summary>
public sealed class MimeContentDisposition
{
    /// <summary>The disposition of a part shown in the body of the message.</summary>
    public const string Inline = "inline";

    /// <summary>The disposition of a part the user saves or opens.</summary>
    public const string Attachment = "attachment";

    /// <summary>Initializes a new instance of the <see cref="MimeContentDisposition"/> class.</summary>
    /// <param name="disposition">The disposition, such as <see cref="Attachment"/>.</param>
    public MimeContentDisposition(string disposition)
    {
        ArgumentNullException.ThrowIfNull(disposition);
        Disposition = disposition;
    }

    /// <summary>Gets or sets the disposition, such as <c>inline</c> or <c>attachment</c>.</summary>
    public string Disposition { get; set; }

    /// <summary>Gets whether the disposition is <c>attachment</c>.</summary>
    public bool IsAttachment => Disposition.Equals(Attachment, StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets the parameters, with case-insensitive names.</summary>
    public IDictionary<string, string> Parameters { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the file name.</summary>
    public string? FileName
    {
        get => Parameters.TryGetValue("filename", out var value) ? value : null;
        set
        {
            if (value is null)
            {
                Parameters.Remove("filename");
            }
            else
            {
                Parameters["filename"] = value;
            }
        }
    }

    /// <summary>Parses the value of a Content-Disposition header.</summary>
    /// <param name="value">The value of the header.</param>
    /// <returns>The disposition.</returns>
    public static MimeContentDisposition Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var tokenizer = new MimeTokenizer(value);
        var disposition = tokenizer.ReadToken(";");
        var result = new MimeContentDisposition(disposition.Length == 0 ? Attachment : disposition);
        foreach (var (name, parameter) in MimeParameters.Parse(ref tokenizer))
        {
            result.Parameters[name] = parameter;
        }

        return result;
    }

    /// <summary>Returns the value of the header.</summary>
    /// <returns>The value of the header.</returns>
    public override string ToString()
    {
        var builder = new StringBuilder(Disposition);
        MimeParameters.Format(builder, Parameters);
        return builder.ToString();
    }
}
