namespace Meziantou.Framework.Imaging.Internals;

/// <summary>One image file directory of the SubIFD tree of a DNG file.</summary>
/// <param name="Directory">The directory.</param>
/// <param name="Index">Its index in the depth-first walk (0 for IFD 0).</param>
/// <param name="Parent">The index of the directory that references it, or -1 for IFD 0.</param>
/// <param name="SubfileType">Its <c>NewSubFileType</c>.</param>
internal sealed record DngDirectory(TiffDirectory Directory, int Index, int Parent, uint SubfileType);
