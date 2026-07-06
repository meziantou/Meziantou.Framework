using System.Reflection;

namespace Meziantou.Framework.Yamlish.Converters;

internal sealed class CSharpUnionYamlishConverterFactory : YamlishConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);

        return HasUnionAttribute(typeToConvert)
            && typeToConvert.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance, binder: null, returnType: typeof(object), types: Type.EmptyTypes, modifiers: null)?.CanRead is true;
    }

    public override YamlishConverter? CreateConverter(Type typeToConvert, YamlishSerializerOptions options)
    {
        return (YamlishConverter)Activator.CreateInstance(typeof(CSharpUnionYamlishConverter<>).MakeGenericType(typeToConvert))!;
    }

    private static bool HasUnionAttribute(Type type)
    {
        foreach (var attribute in type.GetCustomAttributes(inherit: true))
        {
            if (attribute.GetType().FullName == "System.Runtime.CompilerServices.UnionAttribute")
                return true;
        }

        return false;
    }

    [SuppressMessage("Performance", "CA1812", Justification = "The class is instantiated using Activator.CreateInstance")]
    private sealed class CSharpUnionYamlishConverter<T> : YamlishConverter<T>
    {
        private readonly UnionCase[] _cases;
        private readonly PropertyInfo _valueProperty;

        public CSharpUnionYamlishConverter()
        {
            var type = typeof(T);
            _valueProperty = type.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance, binder: null, returnType: typeof(object), types: Type.EmptyTypes, modifiers: null)
                ?? throw new InvalidOperationException($"Cannot serialize the C# union type '{type.FullName}' as the 'Value' property does not exist.");

            var parameterTypes = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .Select(constructor => new
                {
                    Constructor = constructor,
                    Parameters = constructor.GetParameters(),
                })
                .Where(constructor => constructor.Parameters.Length is 1)
                .Select(constructor => new UnionCase(constructor.Parameters[0].ParameterType, GetDiscriminator(constructor.Parameters[0].ParameterType), constructor.Constructor))
                .ToArray();

            if (parameterTypes.Length is 0)
                throw new InvalidOperationException($"Cannot serialize the C# union type '{type.FullName}' as it does not declare any public single-parameter constructors.");

            _cases = parameterTypes;
        }

        public override T? Read(YamlishNode node, YamlishSerializerOptions options)
        {
            if (node is not YamlishMapping mapping)
                throw new FormatException($"Cannot convert a {node.Kind} node to C# union type '{typeof(T).FullName}'.");

            var typeEntry = mapping.FirstOrDefault(entry => options.PropertyNameComparer.Equals(entry.Key, "$type"));
            if (typeEntry.Key is null)
                throw new FormatException($"The C# union type '{typeof(T).FullName}' requires a '$type' discriminator.");

            var valueEntry = mapping.FirstOrDefault(entry => options.PropertyNameComparer.Equals(entry.Key, "Value"));
            if (valueEntry.Key is null)
                throw new FormatException($"The C# union type '{typeof(T).FullName}' requires a 'Value' node.");

            var discriminator = ConverterUtilities.GetScalarValue(typeEntry.Value, typeof(T));
            var unionCase = _cases.FirstOrDefault(unionCase => unionCase.Matches(discriminator));
            if (unionCase is null)
                throw new FormatException($"The C# union type '{typeof(T).FullName}' does not define a case named '{discriminator}'.");

            var value = YamlishSerializer.DeserializeNode(valueEntry.Value, unionCase.Type, options);
            return (T)unionCase.Constructor.Invoke([value]);
        }

        public override YamlishNode Write(T value, YamlishSerializerOptions options)
        {
            var unionValue = _valueProperty.GetValue(value);
            if (unionValue is null)
                throw new InvalidOperationException($"Yamlish does not have a null scalar for C# union type '{typeof(T).FullName}'.");

            var unionCase = _cases.FirstOrDefault(unionCase => unionCase.Type.IsInstanceOfType(unionValue));
            if (unionCase is null)
                throw new InvalidOperationException($"The C# union type '{typeof(T).FullName}' cannot contain a value of type '{unionValue.GetType().FullName}'.");

            return new YamlishMapping
            {
                { "$type", new YamlishScalar(unionCase.Discriminator) },
                { "Value", YamlishSerializer.SerializeToNode(unionValue, unionCase.Type, options) },
            };
        }

        private static string GetDiscriminator(Type type)
        {
            return type.Name;
        }

        private sealed record UnionCase(Type Type, string Discriminator, ConstructorInfo Constructor)
        {
            public bool Matches(string discriminator)
            {
                return string.Equals(discriminator, Discriminator, StringComparison.Ordinal) ||
                    string.Equals(discriminator, Type.FullName, StringComparison.Ordinal) ||
                    string.Equals(discriminator, Type.AssemblyQualifiedName, StringComparison.Ordinal);
            }
        }
    }
}
