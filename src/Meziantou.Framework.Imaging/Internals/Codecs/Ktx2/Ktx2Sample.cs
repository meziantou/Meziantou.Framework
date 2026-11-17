using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A sample of a basic data format descriptor block: its channel, its bits and its qualifiers.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Ktx2Sample(int Channel, int BitOffset, int BitLength, int Qualifiers);
