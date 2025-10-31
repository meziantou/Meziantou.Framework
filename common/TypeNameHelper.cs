using System.Runtime.InteropServices;

namespace Microsoft.Extensions.Logging;
internal static class TypeNameHelper
{
    private const char DefaultNestedTypeDelimiter = '+';

    private static readonly Dictionary<Type, string> BuiltInTypeNames = new()
    {
        { typeof(void), "void" },
        { typeof(bool), "bool" },
        { typeof(byte), "byte" },
        { typeof(char), "char" },
        { typeof(decimal), "decimal" },
        { typeof(double), "double" },
        { typeof(float), "float" },
        { typeof(int), "int" },
        { typeof(long), "long" },
        { typeof(object), "object" },
        { typeof(sbyte), "sbyte" },
        { typeof(short), "short" },
        { typeof(string), "string" },
        { typeof(uint), "uint" },
        { typeof(ulong), "ulong" },
        { typeof(ushort), "ushort" },
    };

    [return: NotNullIfNotNull(nameof(item))]
    public static string? GetTypeDisplayName(object? item, bool fullName = true)
    {
        return item is null ? null : GetTypeDisplayName(item.GetType(), fullName);
    }

    /// <summary>
    /// Pretty print a type name.
    /// </summary>
    /// <param name="type">The <see cref="Type"/>.</param>
    /// <param name="fullName"><see langword="true"/> to print a fully qualified name.</param>
    /// <param name="includeGenericParameterNames"><see langword="true"/> to include generic parameter names.</param>
    /// <param name="includeGenericParameters"><see langword="true"/> to include generic parameters.</param>
    /// <param name="nestedTypeDelimiter">Character to use as a delimiter in nested type names</param>
    /// <returns>The pretty printed type name.</returns>
    public static string GetTypeDisplayName(Type type, bool fullName = true, bool includeGenericParameterNames = false, bool includeGenericParameters = true, char nestedTypeDelimiter = DefaultNestedTypeDelimiter)
    {
        StringBuilder? builder = null;
        var name = ProcessType(ref builder, type, new DisplayNameOptions(fullName, includeGenericParameterNames, includeGenericParameters, nestedTypeDelimiter));
        return name ?? builder?.ToString() ?? "";
    }

    private static string? ProcessType(ref StringBuilder? builder, Type type, in DisplayNameOptions options)
    {
        if (type.IsGenericType)
        {
            var genericArguments = type.GetGenericArguments();
            builder ??= new StringBuilder();
            ProcessGenericType(builder, type, genericArguments, genericArguments.Length, options);
        }
        else if (type.IsArray)
        {
            builder ??= new StringBuilder();
            ProcessArrayType(builder, type, options);
        }
        else if (BuiltInTypeNames.TryGetValue(type, out var builtInName))
        {
            if (builder is null) return builtInName;

            builder.Append(builtInName);
        }
        else if (type.IsGenericParameter)
        {
            if (options.IncludeGenericParameterNames)
            {
                if (builder is null) return type.Name;

                builder.Append(type.Name);
            }
        }
        else
        {
            var name = options.FullName ? type.FullName! : type.Name;

            if (builder is null)
            {
                if (options.NestedTypeDelimiter != DefaultNestedTypeDelimiter)
                {
                    return name.Replace(DefaultNestedTypeDelimiter, options.NestedTypeDelimiter);
                }

                return name;
            }

            builder.Append(name);
            if (options.NestedTypeDelimiter != DefaultNestedTypeDelimiter)
            {
                builder.Replace(DefaultNestedTypeDelimiter, options.NestedTypeDelimiter, builder.Length - name.Length, name.Length);
            }
        }

        return null;
    }

    private static void ProcessArrayType(StringBuilder builder, Type type, in DisplayNameOptions options)
    {
        var innerType = type;
        while (innerType.IsArray)
        {
            innerType = innerType.GetElementType()!;
        }

        ProcessType(ref builder!, innerType, options);

        while (type.IsArray)
        {
            builder.Append('[');
            builder.Append(',', type.GetArrayRank() - 1);
            builder.Append(']');
            type = type.GetElementType()!;
        }
    }

    private static void ProcessGenericType(StringBuilder builder, Type type, Type[] genericArguments, int length, in DisplayNameOptions options)
    {
        var offset = 0;
        if (type.IsNested)
        {
            offset = type.DeclaringType!.GetGenericArguments().Length;
        }

        if (options.FullName)
        {
            if (type.IsNested)
            {
                ProcessGenericType(builder, type.DeclaringType!, genericArguments, offset, options);
                builder.Append(options.NestedTypeDelimiter);
            }
            else if (!string.IsNullOrEmpty(type.Namespace))
            {
                builder.Append(type.Namespace);
                builder.Append('.');
            }
        }

        var genericPartIndex = type.Name.IndexOf('`', StringComparison.Ordinal);
        if (genericPartIndex <= 0)
        {
            builder.Append(type.Name);
            return;
        }

        builder.Append(type.Name, 0, genericPartIndex);

        if (options.IncludeGenericParameters)
        {
            builder.Append('<');
            for (var i = offset; i < length; i++)
            {
                ProcessType(ref builder!, genericArguments[i], options);
                if (i + 1 == length)
                {
                    continue;
                }

                builder.Append(',');
                if (options.IncludeGenericParameterNames || !genericArguments[i + 1].IsGenericParameter)
                {
                    builder.Append(' ');
                }
            }
            builder.Append('>');
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct DisplayNameOptions
    {
        public DisplayNameOptions(bool fullName, bool includeGenericParameterNames, bool includeGenericParameters, char nestedTypeDelimiter)
        {
            FullName = fullName;
            IncludeGenericParameters = includeGenericParameters;
            IncludeGenericParameterNames = includeGenericParameterNames;
            NestedTypeDelimiter = nestedTypeDelimiter;
        }

        public bool FullName { get; }

        public bool IncludeGenericParameters { get; }

        public bool IncludeGenericParameterNames { get; }

        public char NestedTypeDelimiter { get; }
    }
}