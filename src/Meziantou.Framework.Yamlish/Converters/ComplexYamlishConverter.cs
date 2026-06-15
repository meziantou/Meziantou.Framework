using System.Numerics;

namespace Meziantou.Framework.Yamlish;

internal sealed class ComplexYamlishConverter : ScalarYamlishConverter<Complex>
{
    protected override Complex Parse(string value)
    {
        if (value.Length < 5 || value[0] is not '<' || value[^1] is not '>')
            throw new FormatException($"Cannot convert '{value}' to '{typeof(Complex)}'.");

        var separatorIndex = value.IndexOf("; ", StringComparison.Ordinal);
        if (separatorIndex < 0)
            throw new FormatException($"Cannot convert '{value}' to '{typeof(Complex)}'.");

        var real = double.Parse(value.AsSpan(1, separatorIndex - 1), NumberStyles.Float, CultureInfo.InvariantCulture);
        var imaginary = double.Parse(value.AsSpan(separatorIndex + 2, value.Length - separatorIndex - 3), NumberStyles.Float, CultureInfo.InvariantCulture);
        return new Complex(real, imaginary);
    }

    protected override string Format(Complex value) => string.Create(CultureInfo.InvariantCulture, $"<{value.Real}; {value.Imaginary}>");
}
