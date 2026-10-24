# JSONTestSuite

`test_parsing/` is a copy of the directory of https://github.com/nst/JSONTestSuite/tree/1ef36fa01286573e846ac449e8683f8833c5b26a,
licensed under MIT; see LICENSE.txt. The files are kept byte for byte (`.gitattributes` disables the end-of-line
conversion): some of them are not valid UTF-8 or end with a specific whitespace.

The files are named `y_` (must be accepted), `n_` (must be rejected) and `i_` (implementation-defined).
`JsonBuiltinsTests.JsonTestSuite_MatchesV8` decodes each file as a host does, parses it with `JSON.parse`, and compares
the result with the one of V8 (`../Corpus/JSONTestSuiteV8.md`).

To refresh, copy `test_parsing/` of the new commit, update the commit above, and regenerate `../Corpus/JSONTestSuiteV8.jsonl`.
