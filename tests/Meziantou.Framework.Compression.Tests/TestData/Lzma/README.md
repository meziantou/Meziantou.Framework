# LZMA, XZ and lzip test data

The files were generated for this repository by a Python script with xz 5.8.4, the `lzma` module of Python 3 (liblzma)
and 7-Zip 26.06 (`7zz`). The inputs are generated data, so the files carry no third-party content.

## Inputs

| File | Content |
| --- | --- |
| `sample.txt` | 6191 bytes: 120 numbered lines of words chosen by a seeded random generator. The expected output of the XZ, .lzma, lzip and 7z LZMA/LZMA2 files. |
| `filter-input.bin` | 2432 bytes: synthetic machine code with branch instructions of every architecture (x86 E8/E9, PowerPC `bl`, ARM `bl`, ARM-Thumb `bl`, SPARC `call`, ARM64 `bl`/`adrp`, RISC-V `jal`/`auipc`+`addi`, IA-64 bundles with a `br.call`), in 16-byte aligned segments, then the start of `sample.txt`. The expected output of the filter and BCJ2 files. |

## XZ (`xz -c -T1 ...` of `sample.txt` unless stated)

| File | Command |
| --- | --- |
| `xz-check-none.xz`, `xz-check-crc32.xz`, `xz-check-crc64.xz`, `xz-check-sha256.xz` | `xz -C none`, `-C crc32`, `-C crc64`, `-C sha256` |
| `xz-multiblock.xz` | `xz --block-size=1500` (5 blocks) |
| `xz-concatenated-padding.xz` | `xz` of the first half of `sample.txt`, 4 bytes of stream padding, `xz -C crc32` of the second half, 8 bytes of stream padding |
| `xz-empty.xz` | `xz` of an empty input (no block) |
| `xz-filter-x86.xz`, `-powerpc`, `-ia64`, `-arm`, `-armthumb`, `-sparc`, `-arm64`, `-riscv` | `xz --x86 --lzma2=preset=6` (and `--powerpc`, `--ia64`, `--arm`, `--armthumb`, `--sparc`, `--arm64`, `--riscv`) of `filter-input.bin` |
| `xz-filter-delta.xz` | `xz --delta=dist=4 --lzma2=preset=6` of `filter-input.bin` |
| `xz-corrupt-check.xz` | `xz-check-crc64.xz` with the first byte of the CRC64 of the block flipped |

## .lzma

| File | Command |
| --- | --- |
| `lzma-unknown-size.lzma` | `xz --format=lzma` (unknown size, end marker) |
| `lzma-known-size.lzma` | The 13-byte header with the size, then `lzma.compress(format=FORMAT_RAW, filters=[LZMA1 with dict_size=65536, lc=3, lp=0, pb=2])` of Python, which ends with an end marker |
| `lzma-corrupt.lzma` | `lzma-unknown-size.lzma` with the byte at offset 100 XOR 0x55 |

## lzip

lzip is not installed: the members are made with Python. A member is `LZIP`, the version 1, the dictionary size byte
(2^16, or 2^12 for the second member of the multi-member file), the raw LZMA1 data of Python (lc=3, lp=0, pb=2, with an
end marker), then the CRC32, the data size and the member size (little-endian).

| File | Content |
| --- | --- |
| `lzip-single.lz` | One member with `sample.txt` |
| `lzip-multimember.lz` | Two members with the halves of `sample.txt` |
| `lzip-corrupt-crc.lz` | `lzip-single.lz` with the first byte of the CRC32 inverted |

## 7z

`7zz a -t7z -mhc=off -mtm=off -mtc=off -mta=off <methods> a.7z data.bin`: the headers are not compressed, so the packed
streams start at offset 32. The offsets, the sizes and the properties below come from the header of each archive, and
are used by the tests and the seeds of the fuzz targets.

| File | Methods | Packed streams |
| --- | --- | --- |
| `7z-lzma.7z` | `-m0=LZMA:d=64k` of `sample.txt` | LZMA at 32 (1382 bytes), properties `5D 00 20 00 00`, 6191 bytes |
| `7z-lzma2.7z` | `-m0=LZMA2:d=64k` of `sample.txt` | LZMA2 at 32 (1389 bytes), dictionary property `02` |
| `7z-bcj2.7z` | `-m0=BCJ2 -m1=LZMA:d=64k -m2=LZMA:d=64k -m3=LZMA:d=64k -mb0:1 -mb0s1:2 -mb0s2:3` of `filter-input.bin` | main (LZMA) at 32 (2004 bytes, 2396 bytes decoded), range coder (raw) at 2036 (11 bytes), call (LZMA) at 2047 (15 bytes, 12 decoded), jump (LZMA) at 2062 (25 bytes, 24 decoded), LZMA properties `5D 00 10 00 00`, 2432 bytes |
