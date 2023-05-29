using Xunit;

namespace Meziantou.Framework.HumanReadable.Tests;

[SuppressMessage("Design", "CA1052:Static holder types should be Static or NotInheritable")]
public class SerializerTestsBase
{
    protected sealed record Validation
    {
        public object Subject { get; init; }
        public string Expected { get; init; }
        public Type Type { get; init; }
        public HumanReadableSerializerOptions Options { get; init; }
    }

    protected static void AssertSerialization(object obj, string expected)
    {
        AssertSerialization(obj, options: null, expected);
    }

    protected static void AssertSerialization(object obj, HumanReadableSerializerOptions options, string expected)
    {
        AssertSerialization(obj, options, type: null, expected);
    }

    protected static void AssertSerialization(object obj, HumanReadableSerializerOptions options, Type type, string expected)
    {
        var text = type == null ? HumanReadableSerializer.Serialize(obj, options) : HumanReadableSerializer.Serialize(obj, type, options);
        Assert.Equal(expected, text, ignoreLineEndingDifferences: true);
    }

    protected static void AssertSerialization(Validation validation)
    {
        AssertSerialization(validation.Subject, validation.Options, validation.Type, validation.Expected);
    }
}
