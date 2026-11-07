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

        // A nested type receives the generic arguments of its declaring types too (e.g. List<int>.Enumerator has one generic argument),
        // but its DeclaringType is always the generic type definition (List<T>)
        var genericArguments = type.IsGenericType ? type.GetGenericArguments() : [];
        var declaringTypeGenericArgumentCount = 0;
        if (!type.IsGenericParameter)
        {
            if (type.DeclaringType != null)
            {
                var declaringType = type.DeclaringType;
                if (declaringType.IsGenericTypeDefinition)
                {
                    declaringTypeGenericArgumentCount = declaringType.GetGenericArguments().Length;
                    if (!type.IsGenericTypeDefinition && genericArguments.Length >= declaringTypeGenericArgumentCount)
                    {
                        try
                        {
                            declaringType = declaringType.MakeGenericType(genericArguments[..declaringTypeGenericArgumentCount]);
                        }
                        catch (ArgumentException)
                        {
                            // Keep the generic type definition when the arguments do not satisfy the constraints
                        }
                    }
                }

                GetHumanDisplayName(sb, declaringType);
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

        if (genericArguments.Length > declaringTypeGenericArgumentCount)
        {
            sb.Append('<');
            var first = true;
            foreach (var genericType in genericArguments.AsSpan(declaringTypeGenericArgumentCount))
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
            if (parameter.IsOut)
            {
                sb.Append("out ");
            }
            else if (HasAttribute(parameter, "System.Runtime.CompilerServices.IsReadOnlyAttribute"))
            {
                sb.Append("in ");
            }
            else if (HasAttribute(parameter, "System.Runtime.CompilerServices.RequiresLocationAttribute"))
            {
                sb.Append("ref readonly ");
            }
            else
            {
                sb.Append("ref ");
            }

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

    // Both attributes encode the type tree in pre-order. TupleElementNamesAttribute lists the names of all the elements of a tuple
    // (including the ones stored in its Rest field) before the names of the nested tuples, and the Rest field of a long tuple
    // has its own unused names. DynamicAttribute has one flag per type of the underlying tree, including the Rest type.
    private static void WriteValueTupleType(StringBuilder sb, Type type, string?[]? tupleNames, IList<bool>? dynamicFlags)
    {
        var nameIndex = 0;
        var dynamicIndex = 1;
        WriteTuple(sb, type, tupleNames, ref nameIndex, dynamicFlags, ref dynamicIndex);

        static void WriteTuple(StringBuilder sb, Type type, string?[]? tupleNames, ref int tupleNameIndex, IList<bool>? dynamicFlags, ref int dynamicFlagIndex)
        {
            var elementNamesIndex = tupleNameIndex;
            tupleNameIndex += GetTupleElementCount(type);

            sb.Append('(');
            var elementIndex = 0;
            WriteElements(sb, type, tupleNames, elementNamesIndex, ref elementIndex, ref tupleNameIndex, dynamicFlags, ref dynamicFlagIndex);
            sb.Append(')');
        }

        static void WriteElements(StringBuilder sb, Type type, string?[]? tupleNames, int elementNamesIndex, ref int elementIndex, ref int tupleNameIndex, IList<bool>? dynamicFlags, ref int dynamicFlagIndex)
        {
            var genericTypes = type.GenericTypeArguments;
            for (var i = 0; i < genericTypes.Length; i++)
            {
                var genericType = genericTypes[i];
                var isDynamic = dynamicFlags is not null && dynamicFlagIndex < dynamicFlags.Count && dynamicFlags[dynamicFlagIndex];
                dynamicFlagIndex += 1;

                if (IsRestField(type, i))
                {
                    // The elements of the Rest field are written inline, as they are part of the same tuple in the source code
                    tupleNameIndex += GetTupleElementCount(genericType);
                    WriteElements(sb, genericType, tupleNames, elementNamesIndex, ref elementIndex, ref tupleNameIndex, dynamicFlags, ref dynamicFlagIndex);
                    continue;
                }

                if (elementIndex > 0)
                {
                    sb.Append(", ");
                }

                if (isDynamic)
                {
                    sb.Append("dynamic");
                }
                else if (IsValueTuple(genericType))
                {
                    WriteTuple(sb, genericType, tupleNames, ref tupleNameIndex, dynamicFlags, ref dynamicFlagIndex);
                }
                else
                {
                    GetHumanDisplayName(sb, genericType);
                }

                var nameIndex = elementNamesIndex + elementIndex;
                if (tupleNames is not null && nameIndex < tupleNames.Length && tupleNames[nameIndex] is { } name)
                {
                    sb.Append(' ');
                    sb.Append(name);
                }

                elementIndex++;
            }
        }

        static int GetTupleElementCount(Type type)
        {
            var genericTypes = type.GenericTypeArguments;
            return IsRestField(type, genericTypes.Length - 1) ? genericTypes.Length - 1 + GetTupleElementCount(genericTypes[^1]) : genericTypes.Length;
        }

        static bool IsRestField(Type type, int index) => index is 7 && type.GetGenericTypeDefinition() == typeof(ValueTuple<,,,,,,,>) && IsValueTuple(type.GenericTypeArguments[7]);
    }

    private static bool IsValueTuple(Type type)
    {
        return type.Namespace == "System" && type.Name.StartsWith("ValueTuple`", StringComparison.Ordinal);
    }

    private static bool HasAttribute(ParameterInfo parameter, string attributeFullName)
    {
        foreach (var attribute in parameter.GetCustomAttributesData())
        {
            if (attribute.AttributeType.FullName == attributeFullName)
                return true;
        }

        return false;
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
