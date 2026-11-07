# JSONTestSuiteV8.jsonl

The results V8 (node 24) gives for the files of JSONTestSuite (`../JSONTestSuite/test_parsing`).
`JsonBuiltinsTests.JsonTestSuite_MatchesV8` replays them against the engine: they set the expected result of the
implementation-defined `i_` files, and the expected values of the `y_` files. V8 accepts every `y_` file and rejects
every `n_` file.

Regenerate with:

```
node JSONTestSuiteV8.generate.js ../JSONTestSuite/test_parsing JSONTestSuiteV8.jsonl
```

The files are bytes: the generator decodes them as a host does (`TextDecoder`, the UTF-8 decode of the Encoding
Standard: a leading BOM is removed, the invalid sequences become U+FFFD), and the test decodes them the same way. For
the files that are not UTF-8 text without a BOM (listed in the test), the result only depends on this decoding.

Each line is a file name (`f`), then either `"e":true` (JSON.parse throws a SyntaxError) or the result (`r`): the
value written by `JSON.stringify`, with the infinite numbers and `-0` written as `{"number":"..."}`. JSON.stringify
escapes the lone surrogates, so the results are well-formed strings.
