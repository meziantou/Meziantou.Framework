namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>A chain of a <c>morx</c> table: its default flags, the settings that change them, and its subtables.</summary>
internal sealed record FontMorxChain(uint DefaultFlags, IReadOnlyList<(int Type, int Setting, uint EnableFlags, uint DisableFlags)> Features, IReadOnlyList<FontMorxSubtable> Subtables);
