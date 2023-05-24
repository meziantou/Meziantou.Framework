using System.Diagnostics;
using System.Reflection;
using Meziantou.Framework.HumanReadable.Utils;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class FSharpValueOptionConverterFactory : HumanReadableConverterFactory
{
    public override bool CanConvert(Type type)
    {
        if (!type.IsGenericType)
            return false;

        var utils = FSharpUtils.Get(type);
        return type.GetGenericTypeDefinition() == utils?.FsharpValueOptionType;
    }

    public override HumanReadableConverter? CreateConverter(Type typeToConvert, HumanReadableSerializerOptions options)
    {
        return (HumanReadableConverter?)Activator.CreateInstance(typeof(FSharpValueOptionConverter<,>).MakeGenericType(typeToConvert, typeToConvert.GenericTypeArguments[0]));
    }

    [SuppressMessage("Performance", "CA1812", Justification = "The class is instantiated using Activator.CreateInstance")]
    private sealed class FSharpValueOptionConverter<T, TValueOption> : HumanReadableConverter<T>
        where TValueOption : struct, IEquatable<TValueOption>
    {
        private readonly PropertyInfo _valueProperty;

        public FSharpValueOptionConverter()
        {
            _valueProperty = typeof(T).GetProperty("Value") ?? throw new HumanReadableSerializerException($"Cannot serialize the F# type '{typeof(T)}' as the 'Value' property does not exist");
        }

        [SuppressMessage("Maintainability", "CA1508:Avoid dead conditional code", Justification = "False-positive")]
        protected override void WriteValue(HumanReadableTextWriter writer, T? value, HumanReadableSerializerOptions options)
        {
            Debug.Assert(value != null);

            if (value.Equals(default(T)))
            {
                writer.WriteNullValue();
            }
            else
            {
                var propertyValue = (TValueOption)_valueProperty.GetValue(value)!;
                HumanReadableSerializer.Serialize(writer, propertyValue, options);
            }
        }
    }
}
