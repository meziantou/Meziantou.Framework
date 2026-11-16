namespace Meziantou.Framework.Compression.Internals.SevenZip;

/// <summary>A bind pair: the output <see cref="OutIndex"/> of a coder is the input <see cref="InIndex"/> of another one.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct SevenZipBindPair(int InIndex, int OutIndex);
