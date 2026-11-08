namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>A subtable of a <c>morx</c> chain: its coverage (orientation, order and type), its flags, and its content after the header.</summary>
internal sealed record FontMorxSubtable(uint Coverage, uint Flags, byte[] Content);
