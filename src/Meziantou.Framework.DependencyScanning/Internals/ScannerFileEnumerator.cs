using System.Diagnostics;
using System.IO.Enumeration;

namespace Meziantou.Framework.DependencyScanning.Internals;

internal sealed class ScannerFileEnumerator<T> : FileSystemEnumerator<FileToScan<T>>
    where T : struct, IEnabledScannersArray
{
    private static readonly FileSystemEntryPredicate TruePredicate = (ref FileSystemEntry entry) => true;

    private T _scanners;
    private readonly ScannerOptions _options;
    private readonly FileSystemEntryPredicate _shouldScan;
    private readonly FileSystemEntryPredicate _shouldRecurse;

    [SuppressMessage("Usage", "MA0099:Use Explicit enum value instead of 0", Justification = "FileAttributes doesn't have a named value for 0")]
    private static EnumerationOptions GetEnumerationOptions(ScannerOptions options)
    {
        return new EnumerationOptions
        {
            RecurseSubdirectories = options.RecurseSubdirectories,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = 0,
        };
    }

    public ScannerFileEnumerator(string directory, ScannerOptions options)
        : base(directory, GetEnumerationOptions(options))
    {
        _options = options;
        _shouldScan = options.ShouldScanFilePredicate ?? TruePredicate;
        _shouldRecurse = options.ShouldRecursePredicate ?? TruePredicate;
    }

    protected override FileToScan<T> TransformEntry(ref FileSystemEntry entry)
    {
        Debug.Assert(!_scanners.IsEmpty, "Scanners is empty");
        return new FileToScan<T>(_scanners, entry.ToFullPath());
    }

    protected override bool ShouldIncludeEntry(ref FileSystemEntry entry)
    {
        if (entry.IsDirectory)
            return false;

        if (!_shouldScan(ref entry))
            return false;

        var scanners = new T();
        for (var i = 0; i < _options.Scanners.Count; i++)
        {
            if (_options.Scanners[i].ShouldScanFile(new CandidateFileContext(entry.RootDirectory, entry.Directory, entry.FileName)))
            {
                scanners.Set(i);
            }
        }

        _scanners = scanners;
        return !scanners.IsEmpty;
    }

    protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry entry)
    {
        return _shouldRecurse(ref entry);
    }
}
