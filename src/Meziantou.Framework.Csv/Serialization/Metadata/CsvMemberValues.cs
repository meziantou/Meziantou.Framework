using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Csv.Serialization.Metadata;

// The errors of CsvMemberValues<T> and Utf8CsvMemberValues<T>, out of their TryRead, which is inlined in the generated members readers
internal static class CsvMemberValues
{
    [DoesNotReturn]
    public static void ThrowMemberTypeMismatch(Type type, int memberIndex, Type valueType)
        => throw new InvalidOperationException($"The member {memberIndex.ToString(CultureInfo.InvariantCulture)} of '{type}' is not of type '{valueType}'.");
}
