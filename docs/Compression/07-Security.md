---
title: Untrusted archives
description: What the library does to extract an archive safely, and the limits to set when the archives come from untrusted sources.
---

## Paths outside of the destination directory

`ExtractToDirectory` throws an `IOException`, before writing the entry, when an entry would be written outside of the
destination directory:

- its path is rooted, or goes up with `..` (the "zip slip" attack);
- it is a symbolic link whose target is absolute or outside of the directory;
- it is a symbolic link whose target goes up with `..` after a link, a file, or a path that does not exist yet: on
  disk, `..` goes up from the target of the link, so a link `b` to `.` followed by a link `c` to `b/..` would point to the
  parent of the directory;
- it is a hard link to a file outside of the directory;
- it would be written through a link, such as a link the archive created before it to a directory.

A link that already exists where a file is extracted is replaced by the file, so that the file is not written to the
target of the link.

The links whose target is inside the directory are created. The setuid, setgid and sticky bits of the Unix permissions are
not restored.

When you extract the entries yourself with
[ExtractToFile](cref:Meziantou.Framework.Compression.ArchiveEntry.ExtractToFile), check the path you build from
[FullName](cref:Meziantou.Framework.Compression.ArchiveEntry.FullName): it comes from the archive.

## Memory

The size of the dictionary of LZMA, the model of PPMd and the window of RAR come from the archive, and can be large.
[ArchiveReaderOptions.MaxMemoryUsage](cref:Meziantou.Framework.Compression.ArchiveReaderOptions.MaxMemoryUsage) limits
what a decoder allocates (2 GiB by default): an archive that needs more throws an `InvalidDataException`. The LZ windows
grow with the data they produce, so a small archive that declares a large dictionary allocates little.

Before .NET 11, which has a streaming Zstandard decoder, the Zstandard data (zip entries and `.tar.zst` archives) are
decoded whole in memory: the compressed data and the decoded data are both limited by `MaxMemoryUsage`.

## Size of the content

An entry can decompress to much more than its compressed size. [Length](cref:Meziantou.Framework.Compression.ArchiveEntry.Length)
is the size the archive declares, and the content stream stops there and checks it; still, check the total size of the
entries, and their number, before extracting an archive from an untrusted source.

## Passwords

The key derivation of 7z and RAR5 is slow on purpose, and its number of iterations comes from the archive. The library
rejects the archives that ask for more than 2<sup>24</sup> iterations, so that a malicious archive cannot make the
derivation run for hours.
