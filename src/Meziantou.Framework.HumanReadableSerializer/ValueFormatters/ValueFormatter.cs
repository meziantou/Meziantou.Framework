namespace Meziantou.Framework.HumanReadable.ValueFormatters;

public abstract class ValueFormatter
{
    public const string JsonMediaTypeName = "application/json";
    public const string XmlMediaTypeName = "application/xml";
    public const string HtmlMediaTypeName = "text/html";
    public const string WwwFormUrlEncodedMediaTypeName = "application/x-www-form-urlencoded";

    public abstract void Format(HumanReadableTextWriter writer, string? value, HumanReadableSerializerOptions options);
}
