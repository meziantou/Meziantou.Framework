namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class EnumerableKeyValuePairConverter : HumanReadableConverter
{
    public override bool CanConvert(Type type) => typeof(IEnumerable<KeyValuePair<string, object>>).IsAssignableFrom(type);

    public override void WriteValue(HumanReadableTextWriter writer, object? value, HumanReadableSerializerOptions options)
    {
        var list = (IEnumerable<KeyValuePair<string, object>>)value;
        var hasItem = false;
        foreach (var prop in list)
        {
            if (!hasItem)
            {
                writer.StartObject();
                hasItem = true;
            }

            var propertyName = prop.Key;
            var propertyValue = prop.Value;
            writer.WritePropertyName(propertyName);
            HumanReadableSerializer.Serialize(writer, propertyValue, options);
        }

        if (hasItem)
        {
            writer.EndObject();
        }
        else
        {
            writer.WriteEmptyObject();
        }
    }
}
