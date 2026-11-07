namespace Meziantou.Framework.Assertions;

/// <param name="expectedSetRole">The role of the expected collection: "superset" for a subset assertion, "subset" for a superset assertion.</param>
internal readonly struct NegativeSetAssertionError(string assertionName, string expectedSetRole, System.Collections.IEnumerable expectedValue, System.Collections.IEnumerable actualValue, string? actualExpression, string? expectedExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string AssertionName { get; } = assertionName;
    public string ExpectedSetRole { get; } = expectedSetRole;
    public System.Collections.IEnumerable ExpectedValue { get; } = expectedValue;
    public System.Collections.IEnumerable ActualValue { get; } = actualValue;
    public string? ActualExpression { get; } = actualExpression;
    public string? ExpectedExpression { get; } = expectedExpression;
}
