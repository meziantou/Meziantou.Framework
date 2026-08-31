using System.Diagnostics;
using Meziantou.Framework.HumanReadable.Utils;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class FSharpDiscriminatedUnionConverter : HumanReadableConverter
{
    public override bool CanConvert(Type type)
    {
        var utils = FSharpUtils.Get(type);
        return utils?.IsUnionType(type) is true;
    }

    public override void WriteValue(HumanReadableTextWriter writer, object? value, Type valueType, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);

        var type = value.GetType();

        // Both probes swallow reflection failures and return null, so they cannot be trusted
        // to succeed here just because CanConvert did.
        var info = FSharpUtils.Get(type)
            ?? throw new HumanReadableSerializerException($"Cannot serialize the F# union type '{type}' as the F# reflection API is not available");

        var unionCase = info.GetUnionCase(type, value)
            ?? throw new HumanReadableSerializerException($"Cannot serialize the F# union type '{type}' as its union case cannot be determined");

        writer.StartObject();
        writer.WritePropertyName("Tag");
        writer.WriteValue(unionCase.Name ?? "");

        foreach (var field in unionCase.GetFields())
        {
            writer.WritePropertyName(field.Name);

            var propertyValue = field.GetValue(value);
            HumanReadableSerializer.Serialize(writer, propertyValue, field.PropertyType, options);
        }

        writer.EndObject();
    }
}
