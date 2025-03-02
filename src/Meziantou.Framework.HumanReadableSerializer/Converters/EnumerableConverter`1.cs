using System.Diagnostics;

namespace Meziantou.Framework.HumanReadable.Converters;

[SuppressMessage("Performance", "CA1812", Justification = "The class is instantiated using Activator.CreateInstance")]
internal sealed class EnumerableConverter<T> : HumanReadableConverter<IEnumerable<T>>
{
    protected override void WriteValue(HumanReadableTextWriter writer, IEnumerable<T>? value, HumanReadableSerializerOptions options)
    {
        WriteValueCore(writer, value, options);
    }

    internal static void WriteValueCore(HumanReadableTextWriter writer, IEnumerable<T>? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);

        var hasItem = false;

        foreach (var item in value)
        {
            if (!hasItem)
            {
                writer.StartArray();
                hasItem = true;
            }

            writer.StartArrayItem();
            HumanReadableSerializer.Serialize(writer, item, item?.GetType() ?? typeof(T), options);
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
