using System.Numerics;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class ComplexConverter : HumanReadableConverter<Complex>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Complex value, HumanReadableSerializerOptions options)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"<{value.Real}; {value.Imaginary}>");
        writer.WriteValue(text);
    }
}
