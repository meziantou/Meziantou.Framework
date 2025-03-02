using System.Diagnostics;
using System.Reflection;

namespace Meziantou.Framework.HumanReadable.Converters;
internal sealed class TargetInvocationExceptionConverter : HumanReadableConverter<TargetInvocationException>
{
    protected override void WriteValue(HumanReadableTextWriter writer, TargetInvocationException? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);

        HumanReadableSerializer.Serialize(writer, value.InnerException, options);
    }
}
