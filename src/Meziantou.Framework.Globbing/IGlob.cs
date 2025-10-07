#if NET472
using Microsoft.IO;
using Microsoft.IO.Enumeration;
#endif

namespace Meziantou.Framework.Globbing;

public interface IGlob
{
    bool IsMatch(ReadOnlySpan<char> directory, ReadOnlySpan<char> filename, PathItemType? itemType);
    bool IsPartialMatch(ReadOnlySpan<char> folderPath, ReadOnlySpan<char> filename);

    IEnumerable<string> EnumerateFiles(string directory, EnumerationOptions? options = null);
    IEnumerable<string> EnumerateFileSystemEntries(string directory, EnumerationOptions? options = null);
}
