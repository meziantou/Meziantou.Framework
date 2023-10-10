using System.Net;
using System.Text.Encodings.Web;

namespace Meziantou.Framework.Templating;

public class HtmlEmailOutput : Output
{
    public const string TitleSectionName = "title";
    private readonly HtmlEncoder _htmlEncoder;
    private readonly UrlEncoder _urlEncoder;

    private readonly Dictionary<string, string> _sections = new(StringComparer.Ordinal);
    private readonly List<HtmlEmailSection> _currentSections = [];

    public IList<string> ContentIdentifiers { get; } = new List<string>();

    public HtmlEmailOutput(Template template, TextWriter writer) : base(template, writer)
    {
        _htmlEncoder = HtmlEncoder.Default;
        _urlEncoder = UrlEncoder.Default;
    }

    public override void Write(string format, params object?[] args)
    {
        foreach (var currentSection in _currentSections)
        {
            currentSection.Writer.Write(format, args);
        }
        base.Write(format, args);
    }

    public virtual void WriteHtmlEncode(object? value)
    {
        WriteHtmlEncode("{0}", value);
    }

    public virtual void WriteHtmlEncode(string? value)
    {
        WriteHtmlEncode("{0}", value);
    }

    public virtual void WriteHtmlEncode(string format, params object?[] args)
    {
        Write(_htmlEncoder.Encode(string.Format(provider: null, format, args)));
    }

    public virtual void WriteHtmlAttributeEncode(object? value)
    {
        WriteHtmlAttributeEncode("{0}", value);
    }

    public virtual void WriteHtmlAttributeEncode(string? value)
    {
        WriteHtmlAttributeEncode("{0}", value);
    }

    public virtual void WriteHtmlAttributeEncode(string format, params object?[] args)
    {
        Write(_htmlEncoder.Encode(string.Format(provider: null, format, args)));
    }

    public virtual void WriteUrlEncode(object? value)
    {
        WriteUrlEncode("{0}", value);
    }

    public virtual void WriteUrlEncode(string? value)
    {
        WriteUrlEncode("{0}", value);
    }

    public virtual void WriteUrlEncode(string format, params object?[] args)
    {
        var urlEncode = _urlEncoder.Encode(string.Format(provider: null, format, args));
        Write(urlEncode);
    }

    public virtual void WriteContentIdentifier(string cid)
    {
        ArgumentNullException.ThrowIfNull(cid);

        ContentIdentifiers.Add(cid);
        Write("cid:");
        WriteUrlEncode(cid);
    }

    public void BeginSection(string name)
    {
        _currentSections.Add(new HtmlEmailSection(name, new StringWriter()));
    }

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

        if (section != null)
        {
            _sections[section.Name] = section.Writer.ToString();
            _currentSections.Remove(section);
        }
    }

    public string? GetSection(string name)
    {
        if (_sections.TryGetValue(name, out var value))
        {
            return HtmlDecode(value);
        }

        return null;
    }

    protected virtual string? HtmlDecode(string html)
    {
        if (html == null)
            return null;

        return WebUtility.HtmlDecode(html);
    }
}
