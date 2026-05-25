using System.Diagnostics;
using System.Reflection;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class CSharpUnionConverterFactory : HumanReadableConverterFactory
{
    public override bool CanConvert(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return HasUnionAttribute(type)
            && type.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance, binder: null, returnType: typeof(object), types: Type.EmptyTypes, modifiers: null)?.CanRead is true;
    }

    public override HumanReadableConverter? CreateConverter(Type typeToConvert, HumanReadableSerializerOptions options)
    {
        return (HumanReadableConverter)Activator.CreateInstance(typeof(CSharpUnionConverter<>).MakeGenericType(typeToConvert))!;
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
    private sealed class CSharpUnionConverter<T> : HumanReadableConverter<T>
    {
        private readonly PropertyInfo _valueProperty;

        public CSharpUnionConverter()
        {
            _valueProperty = typeof(T).GetProperty("Value", BindingFlags.Public | BindingFlags.Instance, binder: null, returnType: typeof(object), types: Type.EmptyTypes, modifiers: null)
                ?? throw new HumanReadableSerializerException($"Cannot serialize the C# union type '{typeof(T)}' as the 'Value' property does not exist");
        }

        protected override void WriteValue(HumanReadableTextWriter writer, T? value, HumanReadableSerializerOptions options)
        {
            Debug.Assert(value is not null);

            var unionValue = _valueProperty.GetValue(value);
            if (unionValue is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                HumanReadableSerializer.Serialize(writer, unionValue, options);
            }
        }
    }
}
