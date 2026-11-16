using Meziantou.Framework.Compression;
using Meziantou.Framework.FileConversionTool.Archives;

namespace Meziantou.Framework.FileConversionTool.Outputs;

/// <summary>Converts an archive to another archive format: its entries are copied with their names, their times and their permissions.</summary>
internal static class ArchiveOutputWriter
{
    public static void Write(FullPath outputPath, FullPath inputPath, FileFormat outputFormat, ConversionSettings settings, TextWriter error, CancellationToken cancellationToken)
    {
        using var archive = ArchiveFiles.Open(inputPath, settings);
        ArchiveFiles.WriteAtomically(outputPath, stream =>
        {
            using var writer = ArchiveWriter.Create(stream, outputFormat.ArchiveType, settings, storeByDefault: false);
            Copy(archive, writer, message => error.WriteLine($"{inputPath}: {message}"), cancellationToken);
        });
    }

    /// <summary>Copies the entries of an archive, in order, and returns the number of entries written. The links the output format cannot store are reported and skipped.</summary>
    public static int Copy(Archive archive, ArchiveWriter writer, Action<string> warn, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = entry.FullName.TrimEnd('/');
            if (name.Length is 0)
                continue;

            if (entry.IsDirectory)
            {
                writer.AddDirectory(name, entry.LastWriteTime, entry.UnixFileMode);
            }
            else if (entry.LinkTarget is { } linkTarget)
            {
                if (!writer.AddSymbolicLink(name, linkTarget, entry.LastWriteTime))
                {
                    warn($"The symbolic link '{name}' is skipped: the output format cannot store links");
                    continue;
                }
            }
            else if (entry.HardLinkTarget is { } hardLinkTarget)
            {
                if (!writer.AddHardLink(name, hardLinkTarget, entry.LastWriteTime))
                {
                    // The content of the file the link points to is copied
                    var target = archive.GetEntry(hardLinkTarget);
                    if (target is null || target.IsDirectory || target.LinkTarget is not null || target.HardLinkTarget is not null)
                    {
                        warn($"The hard link '{name}' is skipped: the output format cannot store links");
                        continue;
                    }

                    using var targetContent = target.Open();
                    writer.AddFile(name, targetContent, entry.LastWriteTime, entry.UnixFileMode);
                }
            }
            else
            {
                using var content = entry.Open();
                writer.AddFile(name, content, entry.LastWriteTime, entry.UnixFileMode);
            }

            count++;
        }

        return count;
    }
}
