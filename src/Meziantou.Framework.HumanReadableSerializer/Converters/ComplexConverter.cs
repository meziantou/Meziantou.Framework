using System.Globalization;
using System.Numerics;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class ComplexConverter : HumanReadableConverter<Complex>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Complex value, HumanReadableSerializerOptions options)
    {
#if NET6_0_OR_GREATER
        var text = string.Create(CultureInfo.InvariantCulture, $"<{value.Real}; {value.Imaginary}>");
#else
        var text = $"<{value.Real.ToString(CultureInfo.InvariantCulture)}; {value.Imaginary.ToString(CultureInfo.InvariantCulture)}>";
#endif
        writer.WriteValue(text);
    }
}
