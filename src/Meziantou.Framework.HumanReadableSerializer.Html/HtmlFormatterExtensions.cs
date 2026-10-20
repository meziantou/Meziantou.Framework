namespace Meziantou.Framework.HumanReadable.ValueFormatters;

/// <summary>Provides extension methods to register the HTML formatter.</summary>
public static class HtmlFormatterExtensions
{
    /// <summary>Formats the values whose media type is <c>text/html</c>: the value is parsed and serialized back, as defined by the HTML specification.</summary>
    /// <param name="serializerOptions">The options to add the formatter to.</param>
    /// <param name="formatterOptions">The options of the formatter, or <see langword="null"/> to use the default options.</param>
    /// <returns><paramref name="serializerOptions"/>.</returns>
    public static HumanReadableSerializerOptions AddHtmlFormatter(this HumanReadableSerializerOptions serializerOptions, HtmlFormatterOptions? formatterOptions = null)
    {
        serializerOptions.AddFormatter(ValueFormatter.HtmlMediaTypeName, new HtmlFormatter(formatterOptions ?? new()));
        return serializerOptions;
    }
}
