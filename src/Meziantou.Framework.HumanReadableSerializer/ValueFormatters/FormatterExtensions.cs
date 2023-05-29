namespace Meziantou.Framework.HumanReadable.ValueFormatters;

public static class FormatterExtensions
{
    public static HumanReadableSerializerOptions AddJsonFormatter(this HumanReadableSerializerOptions serializerOptions, JsonFormatterOptions? formatterOptions = null)
    {
        serializerOptions.AddFormatter("json", new JsonFormatter(formatterOptions ?? new() { WriteIndented = true }));
        return serializerOptions;
    }

    public static HumanReadableSerializerOptions AddXmlFormatter(this HumanReadableSerializerOptions serializerOptions, XmlFormatterOptions? formatterOptions = null)
    {
        serializerOptions.AddFormatter("xml", new XmlFormatter(formatterOptions ?? new() { WriteIndented = true }));
        return serializerOptions;
    }

    public static HumanReadableSerializerOptions AddHtmlFormatter(this HumanReadableSerializerOptions serializerOptions, HtmlFormatterOptions? formatterOptions = null)
    {
        var formatter = new HtmlFormatter(formatterOptions ?? new());
        serializerOptions.AddFormatter("html", formatter);
        serializerOptions.AddFormatter("htm", formatter);
        return serializerOptions;
    }
    public static HumanReadableSerializerOptions AddUrlEncodedFormFormatter(this HumanReadableSerializerOptions serializerOptions, UrlEncodedFormFormatterOptions? formatterOptions = null)
    {
        serializerOptions.AddFormatter("UrlEncodedForm", new UrlEncodedFormFormatter(formatterOptions ?? new() { PrettyFormat = true }));
        return serializerOptions;
    }
}
