using System.IO.Enumeration;

namespace Meziantou.Framework.DependencyScanning;

/// <summary>
/// Represents a predicate that determines whether a file system entry should be included in the scan.
/// </summary>
/// <param name="entry">The file system entry to evaluate.</param>
/// <returns><see langword="true"/> if the entry should be included; otherwise, <see langword="false"/>.</returns>
public delegate bool FileSystemEntryPredicate(ref FileSystemEntry entry);
