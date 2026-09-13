# Conformance corpora

`JavaScriptConformance.jsonl`, `PcreConformance.jsonl`, and `PosixConformance.jsonl` hold patterns together with the
verdict the real engine gave for them. `RegexConformanceTests` replays every record through the parser.

| File | Engine | How a pattern was compiled |
| --- | --- | --- |
| `JavaScriptConformance.jsonl` | V8 (Node.js 24, Unicode 17) | `new RegExp(pattern, flags)` with no flag, `u`, or `v`; the group count and names come from `new RegExp("(?:" + pattern + ")\|", flags).exec("")` |
| `PcreConformance.jsonl` | PCRE2 10.47 | `pcre2_compile` with `PCRE2_UTF`, and `PCRE2_EXTENDED` when `extended` is true; `PCRE2_INFO_CAPTURECOUNT` and the name table |
| `PosixConformance.jsonl` | glibc 2.36 (Debian 12) | `regcomp` with `REG_EXTENDED` for `ere` and without it for `bre`, under `C.UTF-8`; `re_nsub` |

Each record has `pattern` and `valid`, and, when the engine accepted the pattern, `captures` (the highest group number
for PCRE, the number of groups otherwise) and `names` where the dialect has named groups.

The patterns are hand-written edge cases, one probe per escape letter inside and outside a class, and randomly
generated patterns built from fragments of each dialect's grammar. POSIX records are ASCII only, because glibc's
handling of non-ASCII bracket expressions depends on the locale rather than on the grammar.

## Left out on purpose

A handful of engine behaviours are not what the parser does, and the records that exercise them were dropped:

- **V8 does not follow the specification's rule for duplicate group names.** It accepts `(?<a>x)(?:y|(?<a>z))` and
  `(?<b>(?<b>a)|)`, where both groups can take part in the same match. The parser reports both, as ECMA-262 requires.
- **V8 clamps quantifier bounds before comparing them**, so it accepts `a{99999999999,2147483648}`. The parser
  compares the numbers as written and reports the range as reversed.
- **PCRE2 ignores an empty `\Q\E` or a stray `\E` completely**, so a quantifier after one applies to what came before
  (`[\w]\E+`), and a `^` after one at the start of a class still negates it. The parser keeps them as atoms of their
  own, so the quantifier is reported as having nothing to repeat.
- **glibc reads `\,` as a comma inside an interval**, so `a{2\,}` is `a{2,}` there. The parser does not.
