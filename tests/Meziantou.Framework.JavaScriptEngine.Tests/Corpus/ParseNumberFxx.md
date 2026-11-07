# ParseNumberFxx.txt.gz

A sample of [parse-number-fxx-test-data](https://github.com/nigeltao/parse-number-fxx-test-data) (Apache-2.0, see
`ParseNumberFxx.LICENSE.txt`), the corpus fast_float and other float parsers are validated with. Each line is a decimal
string and the bits of the closest float16, float32 and float64: `3C00 3F800000 3FF0000000000000 1`. Every string is a
StrUnsignedDecimalLiteral (no sign, no `Infinity`, no hexadecimal).

`NumberBuiltinsTests.ParseNumberFxx_Sample` checks that StringToNumber (with and without a `-` sign), parseFloat, the
numeric literals of the lexer and JSON.parse give the float64 bits (the leading zeros are removed for the literals and
JSON, and JSON gets a `0` around a lone decimal point), and that Number::toString gives a string that StringToNumber
parses to the same bits.

The sample has 25,707 of the 5.3 million lines: the hand-picked files in full (freetype-2-7, google-wuffs,
lemire-fast-float, more-test-cases, tencent-rapidjson), one line out of 200 of the other files (one out of 2,000 of
remyoudompheng-fptest-*), and the first line of every power of two. It runs in less than half a second.

## The whole corpus

`NumberBuiltinsTests.ParseNumberFxx_FullCorpus` checks the whole corpus when it is fetched (it is skipped otherwise). The
corpus is pinned in `tests/Meziantou.Framework.JavaScriptEngine.Test262/test262.json`, as an optional suite that CI does
not fetch (a ~100 MB download):

```
dotnet run eng/javascriptengine/fetch-tests.cs parse-number-fxx-test-data
dotnet test --project <repo>/tests/Meziantou.Framework.JavaScriptEngine.Tests -f net10.0 -- --filter-method "*ParseNumberFxx*"
```

The whole corpus takes about 16 s of CPU time (2.5 s on an idle 15-core Mac, as the chunks run in parallel).

Regenerate the sample after changing the pinned commit with:

```
dotnet run eng/javascriptengine/update-parse-number-fxx-sample.cs
```
