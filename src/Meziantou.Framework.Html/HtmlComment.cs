#nullable disable
using System.Diagnostics;
using System.Xml;

namespace Meziantou.Framework.Html;

/// <summary>
/// Represents an HTML comment node (&lt;!-- comment --&gt;).
/// </summary>
/// <example>
/// <code>
/// var doc = new HtmlDocument();
/// var comment = doc.CreateComment();
/// comment.Value = "This is a comment";
/// doc.BodyElement.AppendChild(comment);
/// // Output: &lt;!-- This is a comment --&gt;
/// </code>
/// </example>
[DebuggerDisplay("'{Value}'")]
#if HTML_PUBLIC
public
#else
internal
#endif
sealed class HtmlComment : HtmlNode
{
    private string _value;

    internal HtmlComment(HtmlDocument ownerDocument)
        : base("", "#comment", "", ownerDocument)
    {
    }

    /// <inheritdoc />
    public override HtmlNodeType NodeType => HtmlNodeType.Comment;

    /// <summary>Gets or sets the name of the comment node.</summary>
    /// <value>Always returns "#comment". Setting this property has no effect.</value>
    public override string Name
    {
        get => base.Name;
        set
        {
            // do nothing
        }
    }

    /// <summary>Gets or sets the text content of the comment.</summary>
    /// <value>The comment text without the &lt;!-- and --&gt; delimiters.</value>
    public override string InnerText
    {
        get => Value;
        set
        {
            if (!string.Equals(value, Value, StringComparison.Ordinal))
            {
                Value = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Gets or sets the value of the comment.</summary>
    /// <value>The comment text without the &lt;!-- and --&gt; delimiters.</value>
    public override string Value
    {
        get => _value;
        set
        {
            if (!string.Equals(value, _value, StringComparison.Ordinal))
            {
                _value = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Writes the comment node to a text writer.</summary>
    /// <param name="writer">The text writer to write to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    /// <remarks>The output format is: &lt;!--[value]--&gt;</remarks>
    public override void WriteTo(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.Write("<!--");
        writer.Write(Value);
        writer.Write("-->");
    }

    /// <summary>Writes the content of the comment node to a text writer.</summary>
    /// <param name="writer">The text writer to write to.</param>
    /// <remarks>Comments have no content, so this method does nothing.</remarks>
    public override void WriteContentTo(TextWriter writer)
    {
    }

    /// <summary>Writes the comment node to an XML writer.</summary>
    /// <param name="writer">The XML writer to write to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    public override void WriteTo(XmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteComment(Value);
    }

    /// <summary>Writes the content of the comment node to an XML writer.</summary>
    /// <param name="writer">The XML writer to write to.</param>
    /// <remarks>Comments have no content, so this method does nothing.</remarks>
    public override void WriteContentTo(XmlWriter writer)
    {
    }

    /// <summary>Copies the comment node properties to the specified target node.</summary>
    /// <param name="target">The target node to copy to.</param>
    /// <param name="options">The cloning options.</param>
    public override void CopyTo(HtmlNode target, HtmlCloneOptions options)
    {
        base.CopyTo(target, options);
        var comment = (HtmlComment)target;
        comment._value = _value;
    }
}
