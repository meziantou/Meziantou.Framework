# Third-Party Notices

This project includes third-party data. The original copyright notices and the licenses under which the respective
authors made it available are reproduced below.

---

## Adobe Core14 AFM files

- Project: the font metrics of the standard 14 fonts, Adobe Systems
- Source: copied from Apache PDFBox (`pdfbox/src/main/resources/org/apache/pdfbox/resources/afm`, commit
  99c16b1752d0b24e2c26788ff68686211b0bcef0), which distributes the files of Adobe unchanged
- Files: `Fonts/Afm/*.afm` and `Fonts/Afm/MustRead.html`, embedded in the assembly
- Used for: the widths, the kerning and the encodings of the standard 14 fonts

```
This file and the 14 PostScript(R) AFM files it accompanies may be used, copied, and distributed for any purpose and
without charge, with or without modification, provided that all copyright notices are retained; that the AFM files
are not distributed without this file; that all modifications to this file or any of the AFM files are prominently
noted in the modified file(s); and that this paragraph is not modified. Adobe Systems has no responsibility or
obligation to support the use of the AFM files.
```

---

## Adobe Glyph List

- Project: AGL & AGLFN, Adobe Type Tools
- Source: <https://github.com/adobe-type-tools/agl-aglfn> (commit 4036a9ca80a62f64f9de4f7321a9a045ad0ecfd6)
- Files: `Fonts/GlyphList/glyphlist.txt` and `Fonts/GlyphList/zapfdingbats.txt`, embedded in the assembly; the license is
  `Fonts/GlyphList/LICENSE.md`
- Used for: the Unicode text of the glyph names of simple fonts

```
Copyright 2002-2019 Adobe (http://www.adobe.com/).

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

Redistributions of source code must retain the above copyright notice,
this list of conditions and the following disclaimer.

Redistributions in binary form must reproduce the above copyright
notice, this list of conditions and the following disclaimer in the
documentation and/or other materials provided with the distribution.

Neither the name of Adobe nor the names of its contributors may be
used to endorse or promote products derived from this software without
specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```
