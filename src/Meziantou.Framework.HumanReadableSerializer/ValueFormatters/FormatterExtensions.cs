namespace Meziantou.Framework.HumanReadable.ValueFormatters;

public static class FormatterExtensions
{
    public static HumanReadableSerializerOptions AddJsonFormatter(this HumanReadableSerializerOptions serializerOptions, JsonFormatterOptions? formatterOptions = null)
    {
        serializerOptions.AddFormatter(ValueFormatter.JsonMediaTypeName, new JsonFormatter(formatterOptions ?? new() { WriteIndented = true }));
        return serializerOptions;
    }

    public static HumanReadableSerializerOptions AddXmlFormatter(this HumanReadableSerializerOptions serializerOptions, XmlFormatterOptions? formatterOptions = null)
    {
        serializerOptions.AddFormatter(ValueFormatter.XmlMediaTypeName, new XmlFormatter(formatterOptions ?? new() { WriteIndented = true }));
        return serializerOptions;
    }

    public static HumanReadableSerializerOptions AddHtmlFormatter(this HumanReadableSerializerOptions serializerOptions, HtmlFormatterOptions? formatterOptions = null)
    {
        var formatter = new HtmlFormatter(formatterOptions ?? new());
        serializerOptions.AddFormatter(ValueFormatter.HtmlMediaTypeName, formatter);
        return serializerOptions;
    }
    public static HumanReadableSerializerOptions AddUrlEncodedFormFormatter(this HumanReadableSerializerOptions serializerOptions, UrlEncodedFormFormatterOptions? formatterOptions = null)
    {
        serializerOptions.AddFormatter(ValueFormatter.WwwFormUrlEncodedMediaTypeName, new UrlEncodedFormFormatter(formatterOptions ?? new() { PrettyFormat = true }));
        return serializerOptions;
    }
}
