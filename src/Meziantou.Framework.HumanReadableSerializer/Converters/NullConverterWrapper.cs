namespace Meziantou.Framework.HumanReadable.Converters;
internal sealed class NullConverterWrapper : HumanReadableConverter
{
    private readonly HumanReadableConverter _converter;

    public NullConverterWrapper(HumanReadableConverter converter) => _converter = converter;

    public override bool HandleNull => true;

    public override bool CanConvert(Type type) => _converter.CanConvert(type);

    public override void WriteValue(HumanReadableTextWriter writer, object? value, Type valueType, HumanReadableSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            _converter.WriteValue(writer, value, valueType, options);
        }
    }
}
