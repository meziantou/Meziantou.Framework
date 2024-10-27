#if NETCOREAPP2_0_OR_GREATER || NET471_OR_GREATER
using System.Diagnostics;
using System.Globalization;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class ValueTupleConverter : HumanReadableConverter
{
    public override bool CanConvert(Type type) => typeof(System.Runtime.CompilerServices.ITuple).IsAssignableFrom(type);

    public override void WriteValue(HumanReadableTextWriter writer, object? value, Type valueType, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);

        var tuple = (System.Runtime.CompilerServices.ITuple)value;
        if (tuple.Length == 0)
        {
            writer.WriteEmptyObject();
        }
        else
        {
            writer.StartObject();
            for (var i = 0; i < tuple.Length; i++)
            {
                var tupleItem = tuple[i];
                writer.WritePropertyName("Item" + (i + 1).ToString(CultureInfo.InvariantCulture));
                HumanReadableSerializer.Serialize(writer, tupleItem, options);
            }

            writer.EndObject();
        }
    }
}
#endif