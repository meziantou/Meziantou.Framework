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
        return (HumanReadableConverter)Activator.CreateInstance(typeof(FSharpValueOptionConverter<,>).MakeGenericType(typeToConvert, typeToConvert.GenericTypeArguments[0]))!;
    }

    [SuppressMessage("Performance", "CA1812", Justification = "The class is instantiated using Activator.CreateInstance")]
    private sealed class FSharpValueOptionConverter<T, TOption> : HumanReadableConverter<T>
        where T : class
    {
        private readonly PropertyInfo _valueProperty;

        public FSharpValueOptionConverter()
        {
            _valueProperty = typeof(T).GetProperty("Value")!;
        }

        protected override void WriteValue(HumanReadableTextWriter writer, T? value, HumanReadableSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                var propertyValue = (TOption?)_valueProperty.GetValue(value);
                HumanReadableSerializer.Serialize(writer, propertyValue, options);
            }
        }
    }
}
