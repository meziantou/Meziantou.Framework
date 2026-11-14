using System.Numerics;

namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlComplexConverter : YamlComplexConverterBase<Complex, double>
{
    public static YamlComplexConverter Instance { get; } = new();

    protected override Complex Create(double real, double imaginary) => new(real, imaginary);

    protected override double GetReal(Complex value) => value.Real;

    protected override double GetImaginary(Complex value) => value.Imaginary;

#if NET11_0_OR_GREATER
    // The converter of a Complex<T> whose parts have a built-in converter; the source generator matches the same types.
    // Other parts, such as NFloat, have no YAML representation, so the type is handled like any other struct.
    internal static YamlConverter? CreateGeneric(Type partType)
    {
        if (partType == typeof(double)) return new YamlComplexConverter<double>();
        if (partType == typeof(float)) return new YamlComplexConverter<float>();
        if (partType == typeof(Half)) return new YamlComplexConverter<Half>();
        if (partType == typeof(BFloat16)) return new YamlComplexConverter<BFloat16>();
        if (partType == typeof(Decimal32)) return new YamlComplexConverter<Decimal32>();
        if (partType == typeof(Decimal64)) return new YamlComplexConverter<Decimal64>();
        if (partType == typeof(Decimal128)) return new YamlComplexConverter<Decimal128>();
        return null;
    }
#endif
}

#if NET11_0_OR_GREATER
internal sealed class YamlComplexConverter<T> : YamlComplexConverterBase<Complex<T>, T>
    where T : struct, IFloatingPointIeee754<T>, IMinMaxValue<T>
{
    protected override Complex<T> Create(T real, T imaginary) => new(real, imaginary);

    protected override T GetReal(Complex<T> value) => value.Real;

    protected override T GetImaginary(Complex<T> value) => value.Imaginary;

}
#endif
