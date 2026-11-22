namespace Meziantou.Framework.FileConversionTool.Mobi;

/// <summary>An entry of an index of a book: its name, and the values of its tags.</summary>
internal sealed record MobiIndexEntry(byte[] Name, IReadOnlyDictionary<int, IReadOnlyList<uint>> Tags)
{
    /// <summary>Gets a value of a tag, or null when the entry has no such value.</summary>
    public uint? GetValue(int tag, int index) => Tags.TryGetValue(tag, out var values) && index < values.Count ? values[index] : null;
}
