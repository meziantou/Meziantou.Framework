namespace Meziantou.Framework.DependencyScanning.Internals;

/// <summary>A reference to a variable or a property in a value, such as <c>$(XunitVersion)</c>.</summary>
/// <param name="Start">The offset of the reference in the value.</param>
/// <param name="Length">The length of the reference.</param>
/// <param name="Name">The name of the variable, or <see langword="null"/> when the reference is an expression that cannot be resolved, such as a property function.</param>
internal readonly record struct VariableReference(int Start, int Length, string? Name);
