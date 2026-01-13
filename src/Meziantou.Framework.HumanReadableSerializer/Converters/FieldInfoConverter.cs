using System.Diagnostics;
using System.Reflection;
using Meziantou.Framework.HumanReadable.Utils;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class FieldInfoConverter : HumanReadableConverter<FieldInfo>
{
    protected override void WriteValue(HumanReadableTextWriter writer, FieldInfo? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);
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
