using System.Diagnostics;
using System.Text;

namespace Meziantou.Framework.HumanReadable.Converters;
internal sealed class StringBuilderConverter : HumanReadableConverter<StringBuilder>
{
    protected override void WriteValue(HumanReadableTextWriter writer, StringBuilder? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);
        writer.WriteValue(value.ToString());
    }
}
