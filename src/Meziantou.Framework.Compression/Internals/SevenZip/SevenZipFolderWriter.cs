using System.IO.Compression;
using System.Security.Cryptography;
using Meziantou.Framework.Compression.Internals.Crypto;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.SevenZip;

/// <summary>
/// Writes the content of entries to folders, one entry at a time: an entry is appended to the current (solid) folder, or
/// starts a new one when the folder is full or uses another filter. Packed folders of another archive can be copied as-is.
/// </summary>
internal sealed class SevenZipFolderWriter
{
    private readonly Stream _output;
    private readonly SevenZipWriterSettings _settings;
    private readonly List<SevenZipFileItem> _currentFiles = [];
    private SevenZipEncoderChain? _chain;
    private SevenZipFilter _currentFilter;

    public SevenZipFolderWriter(Stream output, SevenZipWriterSettings settings)
    {
        _output = output;
        _settings = settings;
    }

    /// <summary>Gets the folders written so far, in order.</summary>
    public List<SevenZipFolder> Folders { get; } = [];

    /// <summary>Gets the files with content written so far, in the order of the folders.</summary>
    public List<SevenZipFileItem> StreamFiles { get; } = [];

    public SevenZipEntryWriteStream OpenEntry(SevenZipFileItem item)
    {
        return new SevenZipEntryWriteStream(this, item);
    }

    /// <summary>Called on the first byte of an entry: continues the current folder or starts a new one.</summary>
    internal Stream BeginContent(SevenZipFileItem item)
    {
        var filter = _settings.ResolveFilter(item.Name);
        if (_chain is not null && filter != _currentFilter)
        {
            CloseFolder();
        }

        if (_chain is null)
        {
            _chain = new SevenZipEncoderChain(_output, _settings.GetContentSpecs(filter));
            _currentFilter = filter;
        }

        return _chain.Input;
    }

    /// <summary>Called when an entry is complete.</summary>
    internal void EndEntry(SevenZipFileItem item, long length, uint crc)
    {
        item.Size = length;
        if (length is 0)
        {
            item.HasStream = false;
            item.Crc = null;
            return;
        }

        item.HasStream = true;
        item.Crc = crc;
        _currentFiles.Add(item);

        var options = _settings.Options;
        if (!options.Solid
            || (options.SolidFileCount > 0 && _currentFiles.Count >= options.SolidFileCount)
            || (options.SolidBlockSize > 0 && _chain!.UnpackedLength >= options.SolidBlockSize))
        {
            CloseFolder();
        }
    }

    /// <summary>Flushes the current folder.</summary>
    public void CloseFolder()
    {
        if (_chain is null)
            return;

        var folder = _chain.Complete();
        _chain = null;
        AddFolder(folder, _currentFiles);
        _currentFiles.Clear();
    }

    /// <summary>Copies the packed streams of a folder of another archive, whose files are <paramref name="files"/>.</summary>
    public void CopyFolder(Stream source, SevenZipFolder folder, IReadOnlyList<SevenZipFileItem> files)
    {
        CloseFolder();
        var offsets = new long[folder.PackSizes.Length];
        for (var i = 0; i < offsets.Length; i++)
        {
            offsets[i] = _output.Position;
            using var window = new BoundedReadStream(source, folder.PackOffsets[i], folder.PackSizes[i]);
            window.CopyTo(_output);
            if (window.IsTruncated)
                throw new InvalidDataException("The 7z archive is truncated.");
        }

        var copy = new SevenZipFolder
        {
            Coders = folder.Coders,
            BindPairs = folder.BindPairs,
            PackedStreams = folder.PackedStreams,
            UnpackSizes = folder.UnpackSizes,
            UnpackCrc = folder.UnpackCrc,
            PackOffsets = offsets,
            PackSizes = [.. folder.PackSizes],
        };
        AddFolder(copy, files);
    }

    private void AddFolder(SevenZipFolder folder, IReadOnlyList<SevenZipFileItem> files)
    {
        folder.UnpackStreamCount = files.Count;
        long offset = 0;
        foreach (var file in files)
        {
            file.Folder = folder;
            file.FolderIndex = Folders.Count;
            file.OffsetInFolder = offset;
            offset += file.Size;
            StreamFiles.Add(file);
        }

        Folders.Add(folder);
    }
}
