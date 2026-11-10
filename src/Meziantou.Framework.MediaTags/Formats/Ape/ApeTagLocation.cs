using System.Runtime.InteropServices;

namespace Meziantou.Framework.MediaTags.Formats.Ape;

/// <summary>The position of an APE tag in a file.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ApeTagLocation(long Start, long ItemsStart, long ItemsLength, uint ItemCount, bool IsVersion1);
