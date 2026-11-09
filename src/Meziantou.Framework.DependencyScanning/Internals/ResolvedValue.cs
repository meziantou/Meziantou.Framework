namespace Meziantou.Framework.DependencyScanning.Internals;

/// <summary>A value once its variables are replaced with their values.</summary>
/// <param name="Value">The resolved value, or <see langword="null"/> when a variable cannot be resolved.</param>
/// <param name="Location">The location of the definition of the variable when the value is exactly one reference to it, or <see langword="null"/>.</param>
/// <param name="VariableName">The name of the variable when the value is exactly one reference to it, or <see langword="null"/>.</param>
internal readonly record struct ResolvedValue(string? Value, Location? Location, string? VariableName);
