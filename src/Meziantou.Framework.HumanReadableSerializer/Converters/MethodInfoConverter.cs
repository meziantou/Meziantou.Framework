using System.Reflection;
using Meziantou.Framework.HumanReadable.Utils;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class MethodInfoConverter : HumanReadableConverter<MethodInfo>
{
    protected override void WriteValue(HumanReadableTextWriter writer, MethodInfo value, HumanReadableSerializerOptions options)
    {
        var sb = new StringBuilder();
        if (value.IsStatic)
        {
            sb.Append("static ");
        }

        var length = sb.Length;
        TypeUtils.GetHumanDisplayName(sb, value.DeclaringType);
        if (sb.Length != length)
        {
            sb.Append('.');
        }

        sb.Append(value.Name);
        var genericParameters = value.GetGenericArguments();
        if (genericParameters.Length > 0)
        {
            sb.Append('<');
            var isFirst = true;
            foreach (var parameter in genericParameters)
            {
                if (!isFirst)
                {
                    sb.Append(',');
                }

                TypeUtils.GetHumanDisplayName(sb, parameter);
                isFirst = false;
            }

            sb.Append('>');
        }

        var parameters = value.GetParameters();
        sb.Append('(');
        var isFirstParameter = true;
        foreach (var parameter in parameters)
        {
            if (!isFirstParameter)
            {
                sb.Append(',');
            }

            TypeUtils.GetHumanDisplayName(sb, parameter);
            isFirstParameter = false;
        }

        sb.Append(')');

        writer.WriteValue(sb.ToString());
    }
}
