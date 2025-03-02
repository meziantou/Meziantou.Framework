using System.Linq.Expressions;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class ExpressionConverter : HumanReadableConverter<Expression>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Expression value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value.ToString());
    }
}

