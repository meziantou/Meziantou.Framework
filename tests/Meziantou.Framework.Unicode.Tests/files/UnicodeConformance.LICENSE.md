# Unicode conformance test files

The `*.txt.gz` files of this directory are the conformance test files of the Unicode Character Database
and of Unicode IDNA Compatibility Processing 18.0.0, the version the tables of
`Meziantou.Framework.Unicode` are generated from. They are compressed with gzip and otherwise unmodified.

| File | Source | Size (bytes) | SHA-256 of the decompressed content |
| --- | --- | --- | --- |
| `GraphemeBreakTest.txt.gz` | <https://www.unicode.org/Public/18.0.0/ucd/auxiliary/GraphemeBreakTest.txt> | 137,090 | `b0cf047ee94485bbdc846de2b902f5f8a815f6b674f9d04223cddadd91c9df31` |
| `WordBreakTest.txt.gz` | <https://www.unicode.org/Public/18.0.0/ucd/auxiliary/WordBreakTest.txt> | 322,136 | `3dd70c071781276067c680d87303f60434adce7b067bd063194af374edad86a5` |
| `SentenceBreakTest.txt.gz` | <https://www.unicode.org/Public/18.0.0/ucd/auxiliary/SentenceBreakTest.txt> | 87,946 | `84ca7516e7bd1203ab7e8d888ae77f0a0af0a254d10e09a2847000588848885c` |
| `LineBreakTest.txt.gz` | <https://www.unicode.org/Public/18.0.0/ucd/auxiliary/LineBreakTest.txt> | 3,178,750 | `fd9ff9eca411080085b41471bba79ae19d9db2e16924f9770b8509f1affd3f95` |
| `BidiTest.txt.gz` | <https://www.unicode.org/Public/18.0.0/ucd/BidiTest.txt> | 7,959,988 | `9af2f882a4ab50912e388f069a673b94eacd82fa6d07d20a3ff7f3c759e905aa` |
| `BidiCharacterTest.txt.gz` | <https://www.unicode.org/Public/18.0.0/ucd/BidiCharacterTest.txt> | 6,880,771 | `045b24d2c8ab066951bd32fe8c6b4de34647f72b5b1c7df0265f24ab53573e01` |
| `NormalizationTest.txt.gz` | <https://www.unicode.org/Public/18.0.0/ucd/NormalizationTest.txt> | 2,863,708 | `25a50d816764b04abfb4a646d3eb2b2a803284c3873d9a06757b94fe4513dde3` |
| `IdnaTestV2.txt.gz` | <https://www.unicode.org/Public/18.0.0/idna/IdnaTestV2.txt> | 776,565 | `0236b75c5b20dfd857b3b5cf75509887959d6ab00d6c7bda9b7dc3df518c1fde` |

Do not edit these files or this notice by hand: they are written by
`tools/Meziantou.Framework.Unicode.Generator`, which replaces them when the pinned version changes
(`dotnet run --project tools/Meziantou.Framework.Unicode.Generator`).

## License

The files are distributed under the Unicode License v3 (<https://www.unicode.org/license.txt>):

```text
UNICODE LICENSE V3

COPYRIGHT AND PERMISSION NOTICE

Copyright © 1991-2026 Unicode, Inc.

NOTICE TO USER: Carefully read the following legal agreement. BY
DOWNLOADING, INSTALLING, COPYING OR OTHERWISE USING DATA FILES, AND/OR
SOFTWARE, YOU UNEQUIVOCALLY ACCEPT, AND AGREE TO BE BOUND BY, ALL OF THE
TERMS AND CONDITIONS OF THIS AGREEMENT. IF YOU DO NOT AGREE, DO NOT
DOWNLOAD, INSTALL, COPY, DISTRIBUTE OR USE THE DATA FILES OR SOFTWARE.

Permission is hereby granted, free of charge, to any person obtaining a
copy of data files and any associated documentation (the "Data Files") or
software and any associated documentation (the "Software") to deal in the
Data Files or Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, and/or sell
copies of the Data Files or Software, and to permit persons to whom the
Data Files or Software are furnished to do so, provided that either (a)
this copyright and permission notice appear with all copies of the Data
Files or Software, or (b) this copyright and permission notice appear in
associated Documentation.

THE DATA FILES AND SOFTWARE ARE PROVIDED "AS IS", WITHOUT WARRANTY OF ANY
KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT OF
THIRD PARTY RIGHTS.

IN NO EVENT SHALL THE COPYRIGHT HOLDER OR HOLDERS INCLUDED IN THIS NOTICE
BE LIABLE FOR ANY CLAIM, OR ANY SPECIAL INDIRECT OR CONSEQUENTIAL DAMAGES,
OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS,
WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION,
ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THE DATA
FILES OR SOFTWARE.

Except as contained in this notice, the name of a copyright holder shall
not be used in advertising or otherwise to promote the sale, use or other
dealings in these Data Files or Software without prior written
authorization of the copyright holder.
```
