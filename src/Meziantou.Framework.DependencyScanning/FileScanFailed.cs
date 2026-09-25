namespace Meziantou.Framework.DependencyScanning;

/// <summary>Represents the callback method that is invoked when a file cannot be scanned.</summary>
/// <remarks>
/// <para>
/// The file is skipped, and the scan continues with the next file. The failure is an I/O error while opening or
/// reading the file, or an exception thrown by a scanner, such as for a file it cannot parse. The dependencies
/// reported from the file before the failure are kept.
/// </para>
/// <para>
/// A directory scan or a multi-file scan scans several files concurrently, so the callback can be invoked
/// concurrently from several threads and must be thread-safe.
/// </para>
/// <para>
/// An exception thrown by the callback stops the scan, and the scan method reports it.
/// </para>
/// </remarks>
/// <param name="filePath">The path of the file that could not be scanned.</param>
/// <param name="exception">The exception that stopped the scan of the file.</param>
public delegate void FileScanFailed(string filePath, Exception exception);
