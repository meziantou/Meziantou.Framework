using System.Diagnostics;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class CultureInfoConverter : HumanReadableConverter<CultureInfo>
{
    protected override void WriteValue(HumanReadableTextWriter writer, CultureInfo? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);

        // Other instances of the invariant culture exist, e.g. new CultureInfo("")
        if (value.Name.Length is 0)
        {
            writer.WriteValue(value.EnglishName);
        }
        else
        {
            writer.WriteValue(value.Name);
        }
    }
}
