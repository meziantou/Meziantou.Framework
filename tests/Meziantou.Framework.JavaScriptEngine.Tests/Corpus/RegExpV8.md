# RegExpV8.jsonl

Random patterns, flags and inputs, with the results V8 (node 24) gives for them. `RegExpMatcherTests.Exec_MatchesV8`
replays them against the regular expression engine.

Regenerate with:

```
node --regexp-interpret-all RegExpV8.generate.js 1 3000 RegExpV8.jsonl
```

`--regexp-interpret-all` keeps V8 on its bytecode interpreter: its native code gives other results for some patterns
with modifiers groups once it tiers up.

Each line is a pattern (`p`) and its flags (`f`), then either `"e":true` (the constructor throws a SyntaxError) or the
inputs (`t`): the string (`s`), the lastIndex (`i`, used with the `g` and `y` flags) and the result of `exec` (`r`):
`null`, or the index, the captures (`null` for undefined) and the lastIndex after the call. In the strings, `%XXXX` is a
UTF-16 code unit: the JSON readers reject the lone surrogates.

The generator leaves out what V8 does not do as the specification says:

- Under `u` or `v`, a lastIndex inside a surrogate pair, and the matches that start inside a pair: V8 tries some
  positions inside a pair (`/\B/gu` matches between the two halves of an emoji), and the index it reports for a
  lastIndex inside a pair depends on the pattern. The engine starts at the pair, and reports the index of the pair.
- Under `v` and `i`, the property escapes and `\q{...}`: V8 applies MaybeSimpleCaseFolding to a lone General_Category
  value (the specification does not), does not fold `\p{ASCII}`, and does not fold the one-character strings of a
  `\q{...}` that also has longer strings.
- Under `u` or `v`, the modifiers groups: V8 applies the `i` of `(?i:...)` to the rest of the pattern (`\w` matches the
  Kelvin sign in `/\w|(?i:)/u`), or ignores it (`/(?is:\u1E9E)/u` does not match `\u00DF`).
- The duplicate group names V8 accepts but the specification rejects, such as `(?<a>x)(?:(?<a>y))`: the generator
  only duplicates a name in two alternatives of a group.
