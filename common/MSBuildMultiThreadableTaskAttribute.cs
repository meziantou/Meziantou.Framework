namespace Microsoft.Build.Framework;

/// <summary>
/// Marks a task as safe to run in-process in multithreaded builds (<c>-mt</c>, MSBuild 18.6 or later).
/// MSBuild matches this attribute by namespace and name only, so this copy works without referencing MSBuild 18.6,
/// and earlier versions ignore it.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
internal sealed class MSBuildMultiThreadableTaskAttribute : Attribute
{
}
