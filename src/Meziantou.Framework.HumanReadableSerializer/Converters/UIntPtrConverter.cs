namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class UIntPtrConverter : HumanReadableConverter<UIntPtr>
{
    protected override void WriteValue(HumanReadableTextWriter writer, UIntPtr value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value.ToUInt64().ToString(CultureInfo.InvariantCulture));
    }
}

