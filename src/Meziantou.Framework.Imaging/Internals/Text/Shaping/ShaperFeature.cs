using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A feature a shaping model applies, and when.</summary>
/// <param name="Tag">The tag of the feature.</param>
/// <param name="Stage">
/// The stage the lookups of the feature belong to. The lookups of a stage are applied together, in the order of the
/// lookup list; a stage sees the glyphs the stages before it produced.
/// </param>
/// <param name="Flags">How the feature is applied.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ShaperFeature(uint Tag, int Stage, ShaperFeatureFlags Flags = ShaperFeatureFlags.None);
