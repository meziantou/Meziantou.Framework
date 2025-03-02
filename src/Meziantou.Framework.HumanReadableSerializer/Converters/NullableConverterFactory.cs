namespace Meziantou.Framework.HumanReadable.Converters;
internal sealed class NullableConverterFactory : HumanReadableConverterFactory
{
    public override bool CanConvert(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type);
        return underlyingType != null;
    }

    public override HumanReadableConverter? CreateConverter(Type typeToConvert, HumanReadableSerializerOptions options)
    {
        var underlyingType = Nullable.GetUnderlyingType(typeToConvert)!;
        return (HumanReadableConverter)Activator.CreateInstance(typeof(NullableConverter<>).MakeGenericType(underlyingType))!;
    }

    [SuppressMessage("Performance", "CA1812", Justification = "The class is instantiated using Activator.CreateInstance")]
    private sealed class NullableConverter<T> : HumanReadableConverter<T?>
        where T : struct
    {
        protected override void WriteValue(HumanReadableTextWriter writer, T? value, HumanReadableSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                HumanReadableSerializer.Serialize(writer, value.GetValueOrDefault(), options);
            }
        }
    }
}
