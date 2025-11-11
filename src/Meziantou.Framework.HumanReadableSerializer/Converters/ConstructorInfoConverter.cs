using System.Reflection;
using Meziantou.Framework.HumanReadable.Utils;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class ConstructorInfoConverter : HumanReadableConverter<ConstructorInfo>
{
    protected override void WriteValue(HumanReadableTextWriter writer, ConstructorInfo value, HumanReadableSerializerOptions options)
    {
        var sb = new StringBuilder();
        sb.Append(value.IsStatic ? "static " : "new ");
        TypeUtils.GetHumanDisplayName(sb, value.DeclaringType);

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
