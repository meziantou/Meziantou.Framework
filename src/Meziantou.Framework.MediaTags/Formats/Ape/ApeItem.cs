using System.Runtime.InteropServices;

namespace Meziantou.Framework.MediaTags.Formats.Ape;

/// <summary>An item of an APE tag.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ApeItem(string Key, int Flags, ReadOnlyMemory<byte> Value)
{
    public int ItemType => Flags & ApeTag.ItemTypeMask;
}
