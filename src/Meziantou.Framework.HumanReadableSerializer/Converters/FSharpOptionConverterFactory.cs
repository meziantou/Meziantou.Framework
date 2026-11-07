using System.Diagnostics;
using System.Reflection;
using Meziantou.Framework.HumanReadable.Utils;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class FSharpOptionConverterFactory : HumanReadableConverterFactory
{
    public override bool CanConvert(Type type)
    {
        if (!type.IsGenericType)
            return false;

        var utils = FSharpUtils.Get(type);
        return type.GetGenericTypeDefinition() == utils?.FsharpOptionType;
    }

    public override HumanReadableConverter? CreateConverter(Type typeToConvert, HumanReadableSerializerOptions options)
    {
        return (HumanReadableConverter?)Activator.CreateInstance(typeof(FSharpOptionConverter<,>).MakeGenericType(typeToConvert, typeToConvert.GenericTypeArguments[0]));
    }

    [SuppressMessage("Performance", "CA1812", Justification = "The class is instantiated using Activator.CreateInstance")]
    private sealed class FSharpOptionConverter<T, TOption> : HumanReadableConverter<T>
        where T : class
    {
        private readonly PropertyInfo _valueProperty;

        public FSharpOptionConverter()
        {
            _valueProperty = typeof(T).GetProperty("Value") ?? throw new HumanReadableSerializerException($"Cannot serialize the F# type '{typeof(T)}' as the 'Value' property does not exist");
        }

        protected override void WriteValue(HumanReadableTextWriter writer, T? value, HumanReadableSerializerOptions options)
        {
            // None is represented by null, which is written by the NullConverterWrapper
            Debug.Assert(value is not null);

            var propertyValue = (TOption?)_valueProperty.GetValue(value);
            HumanReadableSerializer.Serialize(writer, propertyValue, options);
        }
    }
}
