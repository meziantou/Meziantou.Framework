# Third-Party Notices

This project includes third-party data and code ported from third-party code. The original copyright notices and the
licenses under which the respective authors made them available are reproduced below.

---

## GS1 Barcode Syntax Dictionary

- Project: GS1 Barcode Syntax Dictionary and its reference linters, GS1 AISBL
- Source: <https://github.com/gs1/gs1-syntax-dictionary>
- License: <https://github.com/gs1/gs1-syntax-dictionary/blob/c63d8a12210dd0cfaceac4aae45ccf57b48eb15b/LICENSE> (Apache License, Version 2.0)
- Version: commit c63d8a12210dd0cfaceac4aae45ccf57b48eb15b (2026-08-29)
- Files: `Internal/Gs1/gs1-syntax-dictionary.txt`, unmodified, embedded in the assembly
  (SHA-256 1ea02f4e3b81d3092e095987600e01c7fd55310f4da5181f5a070262ff6ffc94)
- Used for: `Gs1ValidationMode.Dictionary` and `Gs1Element.ValidateAssociations`, the formats of the application
  identifiers and their associations. `Internal/Gs1/Gs1Linters.cs` is a C# port of the reference linters
  (`src/lint_*.c`): the checks, their error codes, positions and messages, and the tables of ISO 3166, ISO 4217, AIDC
  media types and package types. The unit tests of the linters were converted to
  `tests/Meziantou.Framework.Barcodes.Tests/Gs1/gs1-linter-tests.json`.

```
Copyright (c) 2020-2021 BWIPP project. https://bwipp.terryburton.co.uk/
Copyright (c) 2020-2021 Zint Project. https://zint.org.uk/
Copyright (c) 2021-2025 GS1 AISBL

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.

You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
```
