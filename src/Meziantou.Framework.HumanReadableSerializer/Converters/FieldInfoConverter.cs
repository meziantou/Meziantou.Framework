using System.Reflection;
using System.Text;
using Meziantou.Framework.HumanReadable.Utils;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class FieldInfoConverter : HumanReadableConverter<FieldInfo>
{
    protected override void WriteValue(HumanReadableTextWriter writer, FieldInfo value, HumanReadableSerializerOptions options)
    {
        var sb = new StringBuilder();
        if (value.IsStatic)
        {
            sb.Append("static ");
        }

        var length = sb.Length;
        TypeUtils.GetHumanDisplayName(sb, value.DeclaringType);
        if (length != sb.Length)
        {
            sb.Append('.');
        }

        sb.Append(value.Name);
        writer.WriteValue(sb.ToString());
    }
}
