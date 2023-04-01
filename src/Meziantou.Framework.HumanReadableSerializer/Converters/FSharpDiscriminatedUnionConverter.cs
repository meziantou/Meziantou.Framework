using Meziantou.Framework.HumanReadable.Utils;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class FSharpDiscriminatedUnionConverter : HumanReadableConverter
{
    public override bool CanConvert(Type type)
    {
        var utils = FSharpUtils.Get(type);
        return utils?.IsUnionType(type) is true;
    }

    public override void WriteValue(HumanReadableTextWriter writer, object? value, HumanReadableSerializerOptions options)
    {
        var type = value.GetType();
        var info = FSharpUtils.Get(type)!;
        var unionCase = info.GetUnionCase(type, value)!;

        writer.StartObject();
        writer.WritePropertyName("Tag");
        writer.WriteValue(unionCase.Name);

        foreach (var field in unionCase.GetFields())
        {
            writer.WritePropertyName(field.Name);

            var propertyValue = field.GetValue(value);
            HumanReadableSerializer.Serialize(writer, propertyValue, field.PropertyType, options);
        }

        writer.EndObject();
    }
}
