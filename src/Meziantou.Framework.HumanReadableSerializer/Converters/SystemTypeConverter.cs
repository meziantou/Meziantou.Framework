namespace Meziantou.Framework.HumanReadable.Converters;
internal sealed class SystemTypeConverter : HumanReadableConverter<Type>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Type value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value.AssemblyQualifiedName);
    }
}