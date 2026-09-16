using System.Diagnostics;
using System.Linq.Expressions;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class ExpressionConverter : HumanReadableConverter<Expression>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Expression? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);

        // Expression.ToString formats the constants (e.g. 1.5 or a DateTime) with the current culture
        var currentCulture = CultureInfo.CurrentCulture;
        string text;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            text = value.ToString();
        }
        finally
        {
            CultureInfo.CurrentCulture = currentCulture;
        }

        writer.WriteValue(text);
    }
}
