using System.Net;
using System.Text.Encodings.Web;

namespace Meziantou.Framework.Templating;

/// <summary>Provides output writing capabilities for HTML email templates with support for HTML encoding, URL encoding, sections, and content identifiers.</summary>
/// <example>
/// <code>
/// var output = new HtmlEmailOutput(template, writer);
/// output.WriteHtmlEncode("&lt;b&gt;text&lt;/b&gt;"); // Outputs: &amp;lt;b&amp;gt;text&amp;lt;/b&amp;gt;
/// output.WriteUrlEncode("value&amp;param"); // Outputs: value%26param
/// output.WriteContentIdentifier("logo.png"); // Outputs: cid:logo.png
/// </code>
/// </example>
public class HtmlEmailOutput(Template template, TextWriter writer) : Output(template, writer)
{
    /// <summary>The name of the section used for the email title.</summary>
    public const string TitleSectionName = "title";
    private readonly HtmlEncoder _htmlEncoder = HtmlEncoder.Default;
    private readonly UrlEncoder _urlEncoder = UrlEncoder.Default;

    private readonly Dictionary<string, string> _sections = new(StringComparer.Ordinal);
    private readonly List<HtmlEmailSection> _currentSections = [];

    /// <summary>Gets the list of content identifiers (CIDs) referenced in the template.</summary>
    public IList<string> ContentIdentifiers { get; } = new List<string>();

    public override void Write(string format, params object?[] args)
    {
        foreach (var currentSection in _currentSections)
        {
            currentSection.Writer.Write(format, args);
        }

        base.Write(format, args);
    }

    /// <summary>Writes an HTML-encoded value to the output.</summary>
    public virtual void WriteHtmlEncode(object? value)
    {
        WriteHtmlEncode("{0}", value);
    }

    /// <summary>Writes an HTML-encoded string to the output.</summary>
    public virtual void WriteHtmlEncode(string? value)
    {
        WriteHtmlEncode("{0}", value);
    }

    /// <summary>Writes a formatted HTML-encoded string to the output.</summary>
    public virtual void WriteHtmlEncode(string format, params object?[] args)
    {
        Write(_htmlEncoder.Encode(string.Format(provider: null, format, args)));
    }

    /// <summary>Writes an HTML attribute-encoded value to the output.</summary>
    public virtual void WriteHtmlAttributeEncode(object? value)
    {
        WriteHtmlAttributeEncode("{0}", value);
    }

    /// <summary>Writes an HTML attribute-encoded string to the output.</summary>
    public virtual void WriteHtmlAttributeEncode(string? value)
    {
        WriteHtmlAttributeEncode("{0}", value);
    }

    /// <summary>Writes a formatted HTML attribute-encoded string to the output.</summary>
    public virtual void WriteHtmlAttributeEncode(string format, params object?[] args)
    {
        Write(_htmlEncoder.Encode(string.Format(provider: null, format, args)));
    }

    /// <summary>Writes a URL-encoded value to the output.</summary>
    public virtual void WriteUrlEncode(object? value)
    {
        WriteUrlEncode("{0}", value);
    }

    /// <summary>Writes a URL-encoded string to the output.</summary>
    public virtual void WriteUrlEncode(string? value)
    {
        WriteUrlEncode("{0}", value);
    }

    /// <summary>Writes a formatted URL-encoded string to the output.</summary>
    public virtual void WriteUrlEncode(string format, params object?[] args)
    {
        var urlEncode = _urlEncoder.Encode(string.Format(provider: null, format, args));
        Write(urlEncode);
    }

    /// <summary>Writes a content identifier (CID) reference for embedding resources in emails.</summary>
    /// <param name="cid">The content identifier to write.</param>
    public virtual void WriteContentIdentifier(string cid)
    {
        ArgumentNullException.ThrowIfNull(cid);

        ContentIdentifiers.Add(cid);
        Write("cid:");
        WriteUrlEncode(cid);
    }

    /// <summary>Begins a named section for capturing output separately from the main content.</summary>
    /// <param name="name">The name of the section to begin.</param>
    public void BeginSection(string name)
    {
        _currentSections.Add(new HtmlEmailSection(name, new StringWriter()));
    }

    /// <summary>Ends a named section and stores its captured content.</summary>
    /// <param name="name">The name of the section to end.</param>
    public void EndSection(string name)
    {
        HtmlEmailSection? section;
        if (!string.IsNullOrEmpty(name))
        {
            section = _currentSections.LastOrDefault(_ => string.Equals(_.Name, name, StringComparison.Ordinal));
        }
        else
        {
            section = _currentSections.LastOrDefault();
        }

        if (section is not null)
        {
            _sections[section.Name] = section.Writer.ToString();
            _currentSections.Remove(section);
        }
    }

    /// <summary>Retrieves the content of a previously captured section.</summary>
    /// <param name="name">The name of the section to retrieve.</param>
    /// <returns>The decoded content of the section, or <see langword="null"/> if the section does not exist.</returns>
    public string? GetSection(string name)
    {
        if (_sections.TryGetValue(name, out var value))
        {
            return HtmlDecode(value);
        }

        return null;
    }

    /// <summary>Decodes HTML-encoded text.</summary>
    protected virtual string? HtmlDecode(string html)
    {
        if (html is null)
            return null;

        return WebUtility.HtmlDecode(html);
    }
}
