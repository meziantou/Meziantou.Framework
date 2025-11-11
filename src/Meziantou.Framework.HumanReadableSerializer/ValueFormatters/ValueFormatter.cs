namespace Meziantou.Framework.HumanReadable.ValueFormatters;

/// <summary>Base class for formatters that format values based on media type.</summary>
public abstract class ValueFormatter
{
    /// <summary>The media type name for JSON.</summary>
    public const string JsonMediaTypeName = "application/json";

    /// <summary>The media type name for XML.</summary>
    public const string XmlMediaTypeName = "application/xml";

    /// <summary>The media type name for HTML.</summary>
    public const string HtmlMediaTypeName = "text/html";

    /// <summary>The media type name for URL-encoded form data.</summary>
    public const string WwwFormUrlEncodedMediaTypeName = "application/x-www-form-urlencoded";

    /// <summary>The media type name for CSS.</summary>
    public const string CssMediaTypeName = "text/css";

    /// <summary>The media type name for JavaScript.</summary>
    public const string JavascriptMediaTypeName = "text/javascript";

    /// <summary>Formats the specified value and writes it to the writer.</summary>
    /// <param name="writer">The writer to write to.</param>
    /// <param name="value">The value to format.</param>
    /// <param name="options">The serialization options.</param>
    public abstract void Format(HumanReadableTextWriter writer, string? value, HumanReadableSerializerOptions options);
}
