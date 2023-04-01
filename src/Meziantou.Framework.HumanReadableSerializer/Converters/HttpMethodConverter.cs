namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class HttpMethodConverter : HumanReadableConverter<HttpMethod>
{
    protected override void WriteValue(HumanReadableTextWriter writer, HttpMethod? value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value!.Method);
    }
}
