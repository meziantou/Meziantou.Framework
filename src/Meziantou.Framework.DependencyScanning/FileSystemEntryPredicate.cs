using System.IO.Enumeration;

namespace Meziantou.Framework.DependencyScanning;

/// <summary>Represents a predicate for filtering file system entries during directory enumeration.</summary>
/// <param name="entry">The file system entry to evaluate.</param>
/// <returns><see langword="true"/> if the entry should be included; otherwise, <see langword="false"/>.</returns>
public delegate bool FileSystemEntryPredicate(ref FileSystemEntry entry);
