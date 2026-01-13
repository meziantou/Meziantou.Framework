using System.Reflection;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.HumanReadable.Utils;
internal static class TypeUtils
{
    public static IEnumerable<Type> GetAllInterfaces(this Type type)
    {
        if (type.IsInterface)
            yield return type;

        foreach (var iface in type.GetInterfaces())
        {
            yield return iface;
        }
    }

    public static string GetHumanDisplayName(Type type)
    {
        var sb = new StringBuilder();
        GetHumanDisplayName(sb, type);
        return sb.ToString();
    }

    public static void GetHumanDisplayName(StringBuilder sb, Type? type)
    {
        if(type is null)
        {
            sb.Append("<UnknownType>");
            return;
        }

        if (!type.IsGenericParameter)
        {
            if (type.DeclaringType != null)
            {
                GetHumanDisplayName(sb, type.DeclaringType);
                sb.Append('+');
            }
            else if (type.Namespace is not null)
            {
                sb.Append(type.Namespace);
                sb.Append('.');
            }
        }
        else
        {
            if (type.GenericParameterAttributes == GenericParameterAttributes.Covariant)
            {
                sb.Append("out ");
            }

            if (type.GenericParameterAttributes == GenericParameterAttributes.Contravariant)
            {
                sb.Append("in ");
            }
        }

        var index = type.Name.IndexOf('`', StringComparison.Ordinal);
        if (index != -1)
        {
            sb.Append(type.Name.AsSpan(0, index));
        }
        else
        {
            sb.Append(type.Name);
        }

        if (type.IsGenericType)
        {
            sb.Append('<');
            var first = true;
            foreach (var genericType in type.GetGenericArguments())
            {
                if (!first)
                {
                    sb.Append(", ");
                }

                GetHumanDisplayName(sb, genericType);
                first = false;
            }

            sb.Append('>');
        }
    }

    public static void GetHumanDisplayName(StringBuilder sb, ParameterInfo parameter)
    {
        if (parameter.ParameterType.IsByRef && parameter.IsOut)
        {
            sb.Append("out ");
        }
        else if (parameter.ParameterType.IsByRef && !parameter.IsOut)
        {
            sb.Append("ref ");
        }

        var dynamics = parameter.GetCustomAttribute<DynamicAttribute>();
        if (IsValueTuple(parameter.ParameterType))
        {
            var names = GetTupleElementNames(parameter);
            WriteValueTupleType(sb, parameter.ParameterType, names, dynamics?.TransformFlags);
        }
        else if (dynamics is { TransformFlags: null or [] or [true] })
        {
            sb.Append("dynamic");
        }
        else
        {
            GetHumanDisplayName(sb, parameter.ParameterType);
        }

        if (parameter.Name is not null)
        {
            sb.Append(' ');
            sb.Append(parameter.Name);
        }
    }

    private static void WriteValueTupleType(StringBuilder sb, Type type, string?[]? tupleNames, IList<bool>? dynamicFlags)
    {
        var nameIndex = 0;
        var dynamicIndex = 1;
        WriteValueTupleType(sb, type, tupleNames, ref nameIndex, dynamicFlags, ref dynamicIndex);

        static void WriteValueTupleType(StringBuilder sb, Type type, string?[]? tupleNames, ref int tupleNameIndex, IList<bool>? dynamicFlags, ref int dynamicFlagIndex)
        {
            sb.Append('(');
            var index = 0;
            foreach (var genericType in type.GenericTypeArguments)
            {
                var currentName = tupleNames is not null && tupleNameIndex < tupleNames.Length ? tupleNames[tupleNameIndex] : null;
                var isDynamic = dynamicFlags is not null && dynamicFlagIndex < dynamicFlags.Count && dynamicFlags[dynamicFlagIndex];

                dynamicFlagIndex += 1;
                tupleNameIndex += 1;

                if (index > 0)
                {
                    sb.Append(", ");
                }

                if (isDynamic)
                {
                    sb.Append("dynamic");
                }
                else
                {
                    if (IsValueTuple(genericType))
                    {
                        WriteValueTupleType(sb, genericType, tupleNames, ref tupleNameIndex, dynamicFlags, ref dynamicFlagIndex);
                    }
                    else
                    {
                        GetHumanDisplayName(sb, genericType);
                    }
                }

                if (currentName is not null)
                {
                    sb.Append(' ');
                    sb.Append(currentName);
                }

                index++;
            }

            sb.Append(')');
        }
    }

    private static bool IsValueTuple(Type type)
    {
        return type.Namespace == "System" && type.Name.StartsWith("ValueTuple`", StringComparison.Ordinal);
    }

    private static string?[]? GetTupleElementNames(ParameterInfo parameter)
    {
        foreach (var attribute in parameter.GetCustomAttributes())
        {
            if (!IsTupleElementNameAttribute(attribute))
                continue;

            var property = attribute.GetType().GetProperty("TransformNames", BindingFlags.Instance | BindingFlags.Public);
            if (property == null)
                return null;

            return property.GetValue(attribute) as string?[];
        }

        return null;
    }

    private static bool IsTupleElementNameAttribute(Attribute attribute)
    {
        var attributeType = attribute.GetType();
        return attributeType.Namespace == "System.Runtime.CompilerServices" &&
               attributeType.Name == "TupleElementNamesAttribute";
    }
}
