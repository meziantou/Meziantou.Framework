namespace Meziantou.Framework.Assertions;

/// <param name="expectedSetRole">The role of the expected collection: "superset" for a subset assertion, "subset" for a superset assertion.</param>
internal readonly ref struct CollectionSetAssertionError<T>(string assertionName, string expectedSetRole, CollectionSnapshot<T> expectedValue, CollectionSnapshot<T> actualValue, string? actualExpression, string? expectedExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string AssertionName { get; } = assertionName;
    public string ExpectedSetRole { get; } = expectedSetRole;
    public CollectionSnapshot<T> ExpectedValue { get; } = expectedValue;
    public CollectionSnapshot<T> ActualValue { get; } = actualValue;
    public string? ActualExpression { get; } = actualExpression;
    public string? ExpectedExpression { get; } = expectedExpression;
}
