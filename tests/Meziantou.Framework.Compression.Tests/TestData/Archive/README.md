# Archive test data

Files for the format detection of `ArchiveFile` and `ArchiveReader`, and for the tar support. They were made on macOS
with the tools listed below, from a directory with `hello.txt` (16 bytes), `docs/lines.txt` (400 lines),
`docs/sub/nested.txt` and the symbolic link `link-to-hello` to `hello.txt`, the files dated 2024-01-02 03:04:05 local time.

| File | Command |
| --- | --- |
| `sample.tar` | `COPYFILE_DISABLE=1 bsdtar --no-mac-metadata --no-xattrs --no-fflags --format pax -cf sample.tar hello.txt docs link-to-hello` (libarchive 3.7) |
| `sample.tar.gz` | `gzip -9` |
| `sample.tar.bz2` | `bzip2 -9` |
| `sample.tar.xz` | `xz -6` (xz 5.8) |
| `sample.tar.lzma` | `xz --format=lzma` |
| `sample.tar.lz` | the same `bsdtar` command with `--lzip` |
| `sample.tar.zst` | `zstd -19` |
| `sample.tar.Z` | `compress` (macOS) |
| `traversal.tar` | Python `tarfile`: `ok.txt`, then `../evil.txt` |
| `symlink-escape.tar` | Python `tarfile`: a symbolic link to `../../etc/passwd` |
| `symlink-chain.tar` | Python `tarfile`: the links `b` to `.` and `a` to `b/..`, then the file `a/evil.txt`, which `a` would put next to the destination directory |
| `hardlink.tar` | Python `tarfile`, GNU format: `original.txt` and the hard link `copy.txt` |
| `hello.txt.gz` | Python `gzip.compress`: a gzip file that is not a tar archive |
