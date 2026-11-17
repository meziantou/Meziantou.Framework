# Third-Party Notices

This project includes third-party data. The original copyright notices and the licenses under which the respective
authors made it available are reproduced below.

---

## Adobe Core14 AFM files

- Project: the font metrics of the 14 standard fonts of PDF, Adobe Systems
- Source: `Core14_AFMs.zip`, as Adobe published it
  (<https://web.archive.org/web/20091223044748id_/http://partners.adobe.com/public/developer/en/pdf/Core14_AFMs.zip>,
  SHA-256 8c892c3c49553cfd2d2a27c4495b4bb12e2875115be7fd127ed3876df19d8654), unmodified (CRLF line endings)
- Files: `Afm/StandardFonts/*.afm` and `Afm/StandardFonts/MustRead.html`, embedded in the assembly
- Used for: `AfmFontMetrics.GetStandardFont`, the metrics of the standard fonts

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
- Source: <https://github.com/adobe-type-tools/agl-aglfn> (commit 4036a9ca80a62f64f9de4f7321a9a045ad0ecfd6), the same file
  as `src/Meziantou.Framework.Pdf/Fonts/GlyphList/glyphlist.txt`
- Files: `Type1/GlyphList/glyphlist.txt`, embedded in the assembly; the license is `Type1/GlyphList/LICENSE.md`
- Used for: the character map of Type 1 fonts, from the names of their glyphs

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
