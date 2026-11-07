using System.Diagnostics;
using System.IO.Enumeration;

namespace Meziantou.Framework.DependencyScanning.Internals;

/// <summary>Enumerates the files of a directory on the physical disk, with the scanners that apply to each of them.</summary>
/// <remarks>
/// Symbolic links and other reparse points to directories are never followed, so a link cannot make the scan loop or
/// leave the root. A symbolic link to a file is only scanned when its target is inside the root, because updating its
/// dependencies writes to the target.
/// </remarks>
internal sealed class ScannerFileEnumerator<T> : FileSystemEnumerator<FileToScan<T>>
    where T : struct, IEnabledScannersArray
{
    private static readonly FileSystemEntryPredicate TruePredicate = (ref FileSystemEntry entry) => true;

    private T _scanners;
    private readonly string _rootDirectory;
    private readonly ScannerOptions _options;
    private readonly FileSystemEntryPredicate _shouldScan;
    private readonly FileSystemEntryPredicate _shouldRecurse;
    private string? _realRootDirectory;

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
        _rootDirectory = Path.GetFullPath(directory);
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
        for (var i = 0; i < _options.EnabledScanners.Length; i++)
        {
            bool shouldScan;
            try
            {
                shouldScan = _options.EnabledScanners[i].ShouldScanFile(new CandidateFileContext(entry.RootDirectory, entry.Directory, entry.FileName));
            }
            catch (Exception ex)
            {
                _options.OnFileScanFailed?.Invoke(entry.ToFullPath(), ex);
                return false;
            }

            if (shouldScan)
            {
                scanners.Set(i);
            }
        }

        if (scanners.IsEmpty)
            return false;

        if (IsReparsePoint(ref entry) && !IsLinkTargetInRootDirectory(entry.ToFullPath()))
            return false;

        _scanners = scanners;
        return true;
    }

    protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry entry)
    {
        if (IsReparsePoint(ref entry))
            return false;

        return _shouldRecurse(ref entry);
    }

    private static bool IsReparsePoint(ref FileSystemEntry entry) => entry.Attributes.HasFlag(FileAttributes.ReparsePoint);

    private bool IsLinkTargetInRootDirectory(string path)
    {
        // The root can itself be reached through a link, such as /tmp on macOS, so both sides are resolved
        _realRootDirectory ??= ScanPathUtilities.GetRealPath(_rootDirectory) ?? _rootDirectory;
        var target = ScanPathUtilities.GetRealPath(path);
        return target is not null && ScanPathUtilities.IsUnderDirectory(target, _realRootDirectory);
    }
}
