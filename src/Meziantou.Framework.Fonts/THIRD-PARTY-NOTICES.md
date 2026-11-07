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
