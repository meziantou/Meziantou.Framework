namespace Meziantou.Framework.Globbing;

public interface IGlobEvaluatable
{
    bool IsMatch(ReadOnlySpan<char> directory, ReadOnlySpan<char> filename, PathItemType? itemType);
    bool IsPartialMatch(ReadOnlySpan<char> folderPath, ReadOnlySpan<char> filename);

    bool CanMatchFiles { get; }
    bool CanMatchDirectories { get; }
    GlobMode Mode { get; }
    bool TraverseDirectories { get; }
}
