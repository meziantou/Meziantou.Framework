---
title: RAR archives
description: Read RAR5 and RAR 2.9/4.x archives, with their volumes, their encryption and their links.
---

## Read an archive

[RarArchive](cref:Meziantou.Framework.Compression.RarArchive) opens a RAR archive from a seekable stream:

<!-- ref:../../tests/Meziantou.Framework.Compression.Tests/RarArchiveTests.cs;region=RarArchive_Read;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = File.OpenRead(path);
using var archive = new RarArchive(stream);
Assert.Equal(RarFormatVersion.Rar5, archive.FormatVersion);
Assert.True(archive.IsSolid);
foreach (var entry in archive.Entries)
{
    using var content = entry.Open();
    using var buffer = new MemoryStream();
    content.CopyTo(buffer);
    Assert.Equal(entry.Length, buffer.Length);
}
```
<!-- endref -->

[RarFile.ExtractToDirectory](cref:Meziantou.Framework.Compression.RarFile.ExtractToDirectory) extracts an archive file:

<!-- ref:../../tests/Meziantou.Framework.Compression.Tests/RarFileTests.cs;region=RarFile_ExtractToDirectory;format=md-fenced-code-block;dedent=true -->
```csharp
RarFile.ExtractToDirectory(archivePath, destination, overwriteFiles: true);
Assert.True(File.Exists(Path.Combine(destination, "sub/dir1/file1.txt")));
```
<!-- endref -->

- [RarFile.OpenRead](cref:Meziantou.Framework.Compression.RarFile.OpenRead) finds the other volumes of a multi-volume
  archive next to the first one, named `name.part1.rar`, `name.part2.rar`, ... or `name.rar`, `name.r00`, `name.r01`, ...
- A self-extracting archive (an executable followed by the archive) is read as an archive.
- Entries are files, directories, symbolic links ([LinkTarget](cref:Meziantou.Framework.Compression.ArchiveEntry.LinkTarget)),
  hard links and file copies ([HardLinkTarget](cref:Meziantou.Framework.Compression.ArchiveEntry.HardLinkTarget)). The old
  versions of a file that RAR keeps with `-ver` are named `name;1`, `name;2`, ...
- The entries of a solid archive are compressed together: read them in the order of the archive, as `ExtractToDirectory`
  does.

## Encrypted archives

Give the password with [ArchiveReaderOptions.Password](cref:Meziantou.Framework.Compression.ArchiveReaderOptions.Password):

<!-- ref:../../tests/Meziantou.Framework.Compression.Tests/RarArchiveTests.cs;region=RarArchive_Password;format=md-fenced-code-block;dedent=true -->
```csharp
using var archive = RarFile.OpenRead(path, new ArchiveReaderOptions { Password = "password" });
foreach (var entry in archive.Entries)
{
    Assert.True(entry.IsEncrypted);
    using var reader = new StreamReader(entry.Open());
    var content = reader.ReadToEnd();
    Assert.HasCount((int)entry.Length, content);
}
```
<!-- endref -->

RAR5 archives check the password when an entry is opened, or when the archive is opened if its headers are encrypted.
RAR 4 archives cannot check it: a wrong password is detected at the end of the content of an entry, from its CRC.
Both throw an [ArchivePasswordException](cref:Meziantou.Framework.Compression.ArchivePasswordException).

## Limits

- Creating RAR archives is not supported: the RAR compression format is proprietary.
- The archives of RAR 1.5 and the encryption of RAR 2.0 throw a `NotSupportedException`, as the RAR 2.9 archives whose
  filters are not one of the standard filters of WinRAR.
- The NTFS streams, the access lists and the recovery records are ignored.
