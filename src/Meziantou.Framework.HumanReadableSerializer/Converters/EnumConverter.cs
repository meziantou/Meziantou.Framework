using System.Diagnostics;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class EnumConverter : HumanReadableConverter
{
    public override bool CanConvert(Type type) => type.IsEnum;

    public override void WriteValue(HumanReadableTextWriter writer, object? value, Type valueType, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);

        var text = value.ToString() ?? "";

        // A value without a name is formatted as a number using the current culture (e.g. a U+2212 minus sign in sv-SE)
        if (text.Length > 0 && !char.IsLetter(text[0]) && text[0] is not '_')
        {
            var underlyingValue = Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType()), CultureInfo.InvariantCulture);
            text = ((IFormattable)underlyingValue).ToString(format: null, CultureInfo.InvariantCulture);
        }

        writer.WriteValue(text);
    }
}
