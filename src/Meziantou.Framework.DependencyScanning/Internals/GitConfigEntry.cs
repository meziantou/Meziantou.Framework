using System.Runtime.InteropServices;

namespace Meziantou.Framework.DependencyScanning.Internals;

/// <summary>A git config entry. <see cref="Section"/> and <see cref="Key"/> are lowercase; <see cref="Value"/> is <see langword="null"/> for a boolean entry without <c>=</c>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct GitConfigEntry(string Section, string? Subsection, string Key, string? Value);
