# SegmenterV8.jsonl

Random texts with the words V8 (node 24, ICU 78) segments them into with `new Intl.Segmenter('en', { granularity: 'word' })`.
`IntlTests.Segmenter_WordsMatchV8` replays them against the engine.

Most texts are made of words of the word lists of ICU (Chinese and Japanese, Thai, Lao, Khmer, Burmese) mixed with other
characters of their scripts, Hangul, spaces, punctuation, digits, Latin letters, combining marks and emoji, which
exercise the dictionary break engines; the others are random characters, which exercise the word rules.

Regenerate with the word lists of the ICU data release pinned in
`tools/Meziantou.Framework.JavaScriptEngine.Data.Generator/data-versions.json` (`data/brkitr/dictionaries/*.txt` of
`icu4c-<version>-data.zip`):

```
node SegmenterV8.generate.js <directory of the word lists> 1 2000 SegmenterV8.jsonl
```

Each line is a text (`s`) and the result (`r`): the length of each segment in UTF-16 code units, followed by 1 when the
segment is word-like and 0 otherwise. In the texts, `%XXXX` is a UTF-16 code unit: the lone surrogates (the JSON readers
reject them) and `%`.

The generator only uses the characters assigned in the Unicode version of node: the engine may have a newer one.
