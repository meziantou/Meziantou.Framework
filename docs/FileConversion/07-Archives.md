---
title: Archives
description: Convert an archive to another archive format, such as RAR to zip, zip to 7z or .tar.gz to zip, and set the compression and the encryption of the output.
---

The tool converts an archive to another archive format. It copies the files and the folders with their names, their
modification times and their Unix permissions, in the order of the input archive.

| Format | Extensions | Read | Write |
| --- | --- | --- | --- |
| Zip | `.zip` | Yes | Yes |
| 7z | `.7z` | Yes | Yes |
| RAR | `.rar` | Yes | No |
| Tar | `.tar` | Yes | Yes |
| TarGz | `.tar.gz`, `.tgz` | Yes | Yes |
| TarBz2 | `.tar.bz2`, `.tbz2`, `.tbz` | Yes | Yes |
| TarXz | `.tar.xz`, `.txz` | Yes | Yes |
| TarLzma | `.tar.lzma`, `.tlzma` | Yes | Yes |
| TarLz | `.tar.lz`, `.tlz` | Yes | Yes |
| TarZst | `.tar.zst`, `.tzst` | Yes | No |
| TarZ | `.tar.Z`, `.taz` | Yes | No |

The format of an input is detected from its content. Each input archive gives one output archive, next to it by default:

```bash
Meziantou.Framework.FileConversion.Tool backup.rar --output-format zip
Meziantou.Framework.FileConversion.Tool "downloads/*.zip" --output "converted/{name}.tar.xz"
Meziantou.Framework.FileConversion.Tool sources.tar.gz --output-format 7z --archive-compression-level SmallestSize
```

The first command writes `backup.zip`. The name of the output drops the whole extension of the input, so
`sources.tar.gz` gives `sources.7z`.

An EPUB book or a comic book archive converts to an archive format the same way: every file it contains is copied.

## Links

The tar and zip formats store symbolic links, and tar stores hard links: they are copied as links. The 7z output does not
store them: the tool skips the symbolic links, writes the content of the file a hard link points to, and reports each
skipped link on the error output. A tar output does the same with the links whose target contains a null character, which
a zip archive can store (the target of a zip link is the content of its entry), but not a tar archive.

## Compression

| Option | Applies to | Description |
| --- | --- | --- |
| `--archive-compression-level` | zip, 7z, compressed tar | `Optimal` (the default), `Fastest`, `SmallestSize` or `NoCompression` |
| `--zip-compression-method` | zip | `Deflate` (the default), `Stored`, `BZip2`, `Lzma`, `Ppmd`, `Xz` or `Zstandard`. Zstandard needs .NET 11 |
| `--7z-compression-method` | 7z | `Lzma2` (the default), `Lzma`, `BZip2`, `Deflate`, `Ppmd` or `Copy` |
| `--7z-solid` | 7z | Compress the files together, which makes the archive smaller. `true` by default |

## Passwords

`--archive-password` reads the zip, 7z and RAR inputs that are encrypted. A wrong or missing password is an error.

The zip and 7z outputs can be encrypted:

```bash
Meziantou.Framework.FileConversion.Tool private.rar --archive-password "input password" --output-format 7z --7z-password "output password" --7z-encrypt-headers
Meziantou.Framework.FileConversion.Tool data.tar.gz --output-format zip --zip-password "output password"
```

| Option | Description |
| --- | --- |
| `--zip-password` | Encrypt the files of a zip output with this password |
| `--zip-encryption` | `Aes256` (the default), `Aes192`, `Aes128` or `ZipCrypto`. ZipCrypto is weak, but older tools only read this one |
| `--7z-password` | Encrypt the content of a 7z output with this password (AES-256) |
| `--7z-encrypt-headers` | Also encrypt the names of the files of a 7z output, so that the archive cannot be listed without the password |

A password on the command line can be read by the other users of the computer, and is saved in the history of the shell.
