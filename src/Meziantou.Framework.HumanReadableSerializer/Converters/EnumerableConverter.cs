using System.Collections;
namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class EnumerableConverter : HumanReadableConverter
{
    public override bool CanConvert(Type type) => typeof(IEnumerable).IsAssignableFrom(type);

    public override void WriteValue(HumanReadableTextWriter writer, object? value, HumanReadableSerializerOptions options)
    {
        var array = (IEnumerable)value;
        var hasItem = false;

        foreach (var item in array)
        {
            if (!hasItem)
            {
                writer.StartArray();
                hasItem = true;
            }

            writer.StartArrayItem();
            HumanReadableSerializer.Serialize(writer, item, options);
            writer.EndArrayItem();
        }

        if (hasItem)
        {
            writer.EndArray();
        }
        else
        {
            writer.WriteEmptyArray();
        }
    }
}
