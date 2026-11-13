#if NET11_0_OR_GREATER
using System.Numerics;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class ComplexGenericConverterFactory : HumanReadableConverterFactory
{
    public override bool CanConvert(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Complex<>);

    public override HumanReadableConverter? CreateConverter(Type typeToConvert, HumanReadableSerializerOptions options)
    {
        return (HumanReadableConverter?)Activator.CreateInstance(typeof(ComplexConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]));
    }

    // Same format as the converter of the non-generic Complex
    [SuppressMessage("Performance", "CA1812", Justification = "The class is instantiated using Activator.CreateInstance")]
    private sealed class ComplexConverter<T> : HumanReadableConverter<Complex<T>>
        where T : IFloatingPointIeee754<T>, IMinMaxValue<T>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, Complex<T> value, HumanReadableSerializerOptions options)
        {
            var text = string.Create(CultureInfo.InvariantCulture, $"<{value.Real}; {value.Imaginary}>");
            writer.WriteValue(text);
        }
    }
}
#endif
