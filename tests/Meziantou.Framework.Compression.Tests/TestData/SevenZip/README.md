# 7z test archives

The archives are made by 7-Zip 26.06 (`7zz`, macOS arm64) from a directory whose files have the time 2023-11-14T22:13:20Z
(Unix time 1700000000) and the permissions 0644 (directories 0755):

| File | Content |
| --- | --- |
| `hello.txt` | `Hello, 7z!\n` 20 times |
| `sub/data.txt` | `line <i> of the data file\n` for i from 0 to 59 |
| `empty.txt` | empty |
| `emptydir/` | an empty directory |
| `Unicodé-日本.txt` | `Unicode content: é日本\n` (UTF-8) |
| `app.exe` | 600 times `55 48 89 E5 E8 <i * 37, 4 bytes little-endian> 5D C3 90` (x86 code with calls) |
| `link` | a symbolic link to `hello.txt` |
| `random.bin` | 2500 random bytes |

`FILES` is `hello.txt sub empty.txt emptydir Unicodé-日本.txt`. Each command runs in the directory:

| Archive | Command |
| --- | --- |
| `copy.7z` | `7zz a copy.7z -m0=Copy FILES` |
| `lzma.7z` | `7zz a lzma.7z -m0=LZMA FILES` |
| `lzma2.7z` | `7zz a lzma2.7z -m0=LZMA2 FILES` |
| `ppmd.7z` | `7zz a ppmd.7z -m0=PPMd FILES` |
| `bzip2.7z` | `7zz a bzip2.7z -m0=BZip2 FILES` |
| `deflate.7z` | `7zz a deflate.7z -m0=Deflate FILES` |
| `deflate64.7z` | `7zz a deflate64.7z -m0=Deflate64 FILES` |
| `delta.7z` | `7zz a delta.7z -m0=Delta:4 -m1=LZMA2 FILES` |
| `filter-<f>.7z` | `7zz a filter-<f>.7z -m0=<F> -m1=LZMA2 app.exe hello.txt`, for `<F>` in BCJ, PPC, IA64, ARM, ARMT, SPARC, ARM64, RISCV |
| `bcj2.7z` | `7zz a bcj2.7z -m0=BCJ2 -m1=LZMA -m2=LZMA -m3=LZMA -mb0:1 -mb0s1:2 -mb0s2:3 app.exe` |
| `nonsolid.7z` | `7zz a nonsolid.7z -ms=off FILES` |
| `headers-uncompressed.7z` | `7zz a headers-uncompressed.7z -mhc=off FILES` |
| `encrypted.7z` | `7zz a encrypted.7z -ppassword FILES` |
| `encrypted-headers.7z` | `7zz a encrypted-headers.7z -ppassword -mhe=on FILES` |
| `symlink.7z` | `7zz a symlink.7z -snl hello.txt link` |
| `times.7z` | `7zz a times.7z -mtc=on -mta=on -mtm=on FILES` |
| `volumes.7z.001`, `.002`, `.003` | `7zz a volumes.7z -m0=Copy -v1k random.bin hello.txt` |
| `corrupt-crc.7z` | `copy.7z` with the byte at offset 35 (the content of the first file) inverted |
| `truncated.7z` | `lzma2.7z` without its last 20 bytes |
