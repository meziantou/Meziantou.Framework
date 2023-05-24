using System.Diagnostics;

namespace Meziantou.Framework.HumanReadable.Converters;
internal sealed class SystemTypeConverter : HumanReadableConverter<Type>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Type? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value != null);

        if (value.AssemblyQualifiedName != null)
        {
            writer.WriteValue(value.AssemblyQualifiedName);
        }
        else
        {
            throw new HumanReadableSerializerException($"Cannot serialize type '{value}' as its AssemblyQualifiedName is null");
        }
    }
}