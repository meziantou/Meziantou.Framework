using System.Diagnostics;
using System.Globalization;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class MultiDimensionalArrayConverter : HumanReadableConverter
{
    public override bool CanConvert(Type type) => type.IsArray && type.GetArrayRank() > 1;

    public override void WriteValue(HumanReadableTextWriter writer, object? value, Type valueType, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);

        var array = (Array)value;
        var index = 0L;

        var hasItem = false;
        foreach (var item in array)
        {
            if (!hasItem)
            {
                writer.StartArray();
                hasItem = true;
            }

            var indexStr = "";
            var index2 = index;
            for (var rank = array.Rank - 1; rank >= 0; rank--)
            {
                if (indexStr.Length > 0)
                {
                    indexStr = ", " + indexStr;
                }

                var length = array.GetLongLength(rank);
                var rankIndex = index2 % length;
                index2 /= length;

                indexStr = rankIndex.ToString(CultureInfo.InvariantCulture) + indexStr;
            }

            indexStr = "[" + indexStr + "]: ";
            writer.StartArrayItem(indexStr);
            HumanReadableSerializer.Serialize(writer, item, options);
            writer.EndArrayItem();

            index++;
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
