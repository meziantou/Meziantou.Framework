using System.Reflection;
using System.Text;
using Meziantou.Framework.HumanReadable.Utils;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class PropertyInfoConverter : HumanReadableConverter<PropertyInfo>
{
    protected override void WriteValue(HumanReadableTextWriter writer, PropertyInfo value, HumanReadableSerializerOptions options)
    {
        var sb = new StringBuilder();
        if ((value.GetMethod ?? value.SetMethod)?.IsStatic is true)
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
        var parameters = value.GetIndexParameters();
        if (parameters.Length > 0)
        {
            sb.Append('[');
            var isFirst = true;
            foreach (var parameter in parameters)
            {
                if (!isFirst)
                {
                    sb.Append(',');
                }

                TypeUtils.GetHumanDisplayName(sb, parameter.ParameterType);
                if (parameter.Name is not null)
                {
                    sb.Append(' ');
                    sb.Append(parameter.Name);
                }

                isFirst = false;
            }

            sb.Append(']');
        }

        writer.WriteValue(sb.ToString());
    }
}
