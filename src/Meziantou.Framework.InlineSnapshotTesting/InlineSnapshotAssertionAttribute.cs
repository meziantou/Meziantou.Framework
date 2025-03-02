namespace Meziantou.Framework.InlineSnapshotTesting;

[AttributeUsage(AttributeTargets.Method)]
public sealed class InlineSnapshotAssertionAttribute : Attribute
{
    public InlineSnapshotAssertionAttribute(string parameterName) => ParameterName = parameterName;

    public string ParameterName { get; }
}

