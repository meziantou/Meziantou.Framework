using System.Diagnostics;
using System.Text;
using Meziantou.Framework.HumanReadable.Utils;

namespace Meziantou.Framework.HumanReadable.Converters;
internal sealed class SystemTypeConverter : HumanReadableConverter<Type>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Type? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value != null);

        var sb = new StringBuilder();
        TypeUtils.GetHumanDisplayName(sb, value);
        if (value.Assembly != null)
        {
            sb.Append(", ");
            sb.Append(value.Assembly.GetName().Name);
        }

        writer.WriteValue(sb.ToString());
    }
}