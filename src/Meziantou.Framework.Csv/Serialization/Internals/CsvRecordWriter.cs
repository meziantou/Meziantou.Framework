using System.Numerics;
using Meziantou.Framework.Csv.Internals;
using Meziantou.Framework.Csv.Serialization.Metadata;

namespace Meziantou.Framework.Csv.Serialization.Internals;

internal static class CsvRecordWriter
{
    public static void WriteHeader<T, TChar>(CsvWriterCore<TChar> writer, CsvTypeInfo<T> typeInfo)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        if (!typeInfo.Options.HasHeaderRow)
            return;

        foreach (var property in typeInfo.WrittenLayout)
        {
            writer.WriteUtf16Field(property is null ? "" : property.Name);
        }

        writer.EndRow();
    }

    public static void WriteRecord<T, TChar>(CsvWriterCore<TChar> writer, CsvTypeInfo<T> typeInfo, T value)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        if (value is null)
            throw new ArgumentException($"The values to serialize cannot contain null.", nameof(value));

        if (typeInfo.HasOnSerializing)
        {
            ((ICsvOnSerializing)value).OnSerializing();
        }

        foreach (var property in typeInfo.WrittenLayout)
        {
            if (property is null)
            {
                writer.WriteField([], isText: false);
            }
            else if (typeof(TChar) == typeof(char))
            {
                property.Write((CsvWriterCore<char>)(object)writer, value);
            }
            else
            {
                property.Write((CsvWriterCore<byte>)(object)writer, value);
            }
        }

        writer.EndRow();

        if (typeInfo.HasOnSerialized)
        {
            ((ICsvOnSerialized)value).OnSerialized();
        }
    }
}
