namespace Meziantou.Framework.HumanReadable.ValueFormatters;

public static class FormatterExtensions
{
    public static HumanReadableSerializerOptions AddJsonFormatter(this HumanReadableSerializerOptions serializerOptions, JsonFormatterOptions? formatterOptions = null)
    {
        serializerOptions.AddFormatter("application/json", new JsonFormatter(formatterOptions ?? new() { WriteIndented = true }));
        return serializerOptions;
    }

    public static HumanReadableSerializerOptions AddXmlFormatter(this HumanReadableSerializerOptions serializerOptions, XmlFormatterOptions? formatterOptions = null)
    {
        serializerOptions.AddFormatter("application/xml", new XmlFormatter(formatterOptions ?? new() { WriteIndented = true }));
        return serializerOptions;
    }

    public static HumanReadableSerializerOptions AddHtmlFormatter(this HumanReadableSerializerOptions serializerOptions, HtmlFormatterOptions? formatterOptions = null)
    {
        var formatter = new HtmlFormatter(formatterOptions ?? new());
        serializerOptions.AddFormatter("text/html", formatter);
        return serializerOptions;
    }
    public static HumanReadableSerializerOptions AddUrlEncodedFormFormatter(this HumanReadableSerializerOptions serializerOptions, UrlEncodedFormFormatterOptions? formatterOptions = null)
    {
        serializerOptions.AddFormatter("application/x-www-form-urlencoded", new UrlEncodedFormFormatter(formatterOptions ?? new() { PrettyFormat = true }));
        return serializerOptions;
    }
}
