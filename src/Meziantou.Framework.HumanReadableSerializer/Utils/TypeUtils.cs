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

        // The name of these types is based on the name of the element type (e.g. "List`1[]"), so they must be built from it
        if (type.HasElementType)
        {
            GetHumanDisplayName(sb, type.GetElementType());
            if (type.IsArray)
            {
                sb.Append('[');
                var rank = type.GetArrayRank();
                if (rank > 1)
                {
                    sb.Append(',', rank - 1);
                }
                else if (!type.IsSZArray)
                {
                    sb.Append('*');
                }

                sb.Append(']');
            }
            else if (type.IsByRef)
            {
                sb.Append('&');
            }
            else if (type.IsPointer)
            {
                sb.Append('*');
            }

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
            // GenericParameterAttributes also contains the constraints (e.g. "where T : class")
            var variance = type.GenericParameterAttributes & GenericParameterAttributes.VarianceMask;
            if (variance is GenericParameterAttributes.Covariant)
            {
                sb.Append("out ");
            }
            else if (variance is GenericParameterAttributes.Contravariant)
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
        var parameterType = parameter.ParameterType;
        var dynamicAttribute = parameter.GetCustomAttribute<DynamicAttribute>();
        var dynamicFlags = dynamicAttribute?.TransformFlags;
        if (parameterType.IsByRef)
        {
            sb.Append(parameter.IsOut ? "out " : "ref ");
            parameterType = parameterType.GetElementType()!;

            // The by-ref type has its own entry in the dynamic flags
            if (dynamicFlags is { Count: > 0 })
            {
                dynamicFlags = dynamicFlags.Skip(1).ToArray();
            }
        }

        if (IsValueTuple(parameterType))
        {
            var names = GetTupleElementNames(parameter);
            WriteValueTupleType(sb, parameterType, names, dynamicFlags);
        }
        else if (dynamicAttribute is not null && dynamicFlags is null or [] or [true])
        {
            sb.Append("dynamic");
        }
        else
        {
            GetHumanDisplayName(sb, parameterType);
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
