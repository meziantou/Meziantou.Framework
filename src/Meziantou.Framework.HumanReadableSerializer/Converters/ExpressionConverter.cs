using System.Diagnostics;
using System.Linq.Expressions;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class ExpressionConverter : HumanReadableConverter<Expression>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Expression? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);
        writer.WriteValue(value.ToString());
    }
}

