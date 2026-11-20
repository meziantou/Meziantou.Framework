using System.Globalization;
using System.Text;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Fuzz;

/// <summary>
/// The target of the SVG lengths: the input is the value of <c>SVGLength.valueAsString</c>, a length or a value with a
/// substitution function (<c>var()</c>, https://github.com/w3c/svgwg/issues/1038). An accepted value serializes to a value
/// that is parsed as the same length, a substitution has the unknown unit and the value 0 and cannot be converted, and the
/// value of a length without element is finite.
/// </summary>
public static partial class WebPlatformFuzzTargets
{
    private const int SvgLengthMaxLength = 256;

    /// <summary>The tokens of the target: the units, the substitution functions and the custom properties.</summary>
    private static string[] SvgLengthTokens => ["var(", "env(", "attr(", ")", "--x", ",", "%", "px", "em", "ex", "cm", "mm", "in", "pt", "pc", "rem", "q", "e+", ".", "-"];

    /// <summary>Parses the input as the value of a standalone SVG length and checks its serialization and its value.</summary>
    public static void SvgLengthValue(string input)
    {
        var length = new SvgLength();
        try
        {
            length.ValueAsString = input;
        }
        catch (DomException)
        {
            FuzzAssert.False(SvgLength.TryParseSubstitution(input, out _), $"A substitution is rejected: {FuzzAssert.Format(input)}");
            return;
        }

        var serialized = length.ValueAsString;
        var unitType = length.UnitType;
        var copy = new SvgLength { ValueAsString = serialized };
        FuzzAssert.Equal(serialized, copy.ValueAsString, $"The serialization of {FuzzAssert.Format(input)} is not parsed as the same length");
        FuzzAssert.Equal(unitType, copy.UnitType, $"The serialization of {FuzzAssert.Format(input)} does not have the same unit");

        if (SvgLength.TryParseSubstitution(input, out var substitution))
        {
            FuzzAssert.Equal(substitution, serialized, $"A substitution does not serialize as its text: {FuzzAssert.Format(input)}");
            FuzzAssert.Equal(SvgLength.SvgLengthTypeUnknown, unitType, $"A substitution does not have the unknown unit: {FuzzAssert.Format(input)}");
            FuzzAssert.Equal(0f, length.Value, $"The value of a substitution is not 0: {FuzzAssert.Format(input)}");
            try
            {
                length.ConvertToSpecifiedUnits(SvgLength.SvgLengthTypePx);
                FuzzAssert.Fail($"A substitution is converted: {FuzzAssert.Format(input)}");
            }
            catch (DomException)
            {
            }

            return;
        }

        try
        {
            FuzzAssert.True(!float.IsNaN(length.Value), $"The value of {FuzzAssert.Format(input)} is NaN");
        }
        catch (DomException)
        {
            // The relative units other than em, ex and the percentages cannot be resolved without element
        }
    }

    private static IEnumerable<FuzzSeed> GetSvgLengthSeeds()
    {
        string[] values = ["2px", "2%", "1.5em", "-3e2cm", "10Q", "4rem", "var(--w)", " var(--w) ", "calc(var(--w) * 2)", "env(safe-area-inset-top)", "attr(width px)", "my-var(--w)", "--var(x)", "px", "", "1e40px"];
        for (var i = 0; i < values.Length; i++)
        {
            yield return new FuzzSeed("svg-length-" + i.ToString(CultureInfo.InvariantCulture), Encoding.UTF8.GetBytes(values[i]));
        }
    }
}
