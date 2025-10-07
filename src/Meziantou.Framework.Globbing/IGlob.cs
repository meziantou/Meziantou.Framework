#if NET472
using Microsoft.IO;
using Microsoft.IO.Enumeration;
#else
#endif

namespace Meziantou.Framework.Globbing;

public interface IGlob
{
    bool IsMatch(ReadOnlySpan<char> directory, ReadOnlySpan<char> filename, PathItemType? itemType);
    bool IsPartialMatch(ReadOnlySpan<char> folderPath, ReadOnlySpan<char> filename);
}
