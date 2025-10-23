namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class IntPtrConverter : HumanReadableConverter<IntPtr>
{
    protected override void WriteValue(HumanReadableTextWriter writer, IntPtr value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value.ToInt64().ToString(CultureInfo.InvariantCulture));
    }
}

