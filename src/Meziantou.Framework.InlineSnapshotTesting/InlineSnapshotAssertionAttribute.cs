namespace Meziantou.Framework.InlineSnapshotTesting;

/// <summary>
/// Marks a method as an inline snapshot assertion method and specifies the parameter that contains the expected snapshot value.
/// This attribute is used to enable snapshot validation in helper methods.
/// </summary>
/// <example>
/// <code>
/// [InlineSnapshotAssertion(nameof(expected))]
/// static void Helper(object data, string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
/// {
///     InlineSnapshot.Validate(data, expected, filePath, lineNumber);
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InlineSnapshotAssertionAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="InlineSnapshotAssertionAttribute"/> class with the specified parameter name.</summary>
    /// <param name="parameterName">The name of the parameter that contains the expected snapshot value.</param>
    public InlineSnapshotAssertionAttribute(string parameterName) => ParameterName = parameterName;

    /// <summary>Gets the name of the parameter that contains the expected snapshot value.</summary>
    public string ParameterName { get; }
}

