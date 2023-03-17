namespace Meziantou.Framework.HumanReadable.Converters;
internal sealed class NullableConverter : HumanReadableConverter
{
    public override bool CanConvert(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type);
        return underlyingType != null;
    }

    public override void WriteValue(HumanReadableTextWriter writer, object? value, HumanReadableSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
        }
        else
        {
            var actualType = value.GetType();
            var converter = options.GetConverter(actualType);
            converter.WriteValue(writer, value, options);
        }
    }
}
