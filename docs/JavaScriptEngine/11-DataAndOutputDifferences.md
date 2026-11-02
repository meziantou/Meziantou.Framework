---
title: Data versions and output differences
description: The versions of the Unicode, locale and time zone data embedded in the engine, and the results that can differ from Node.js or a browser.
---

## Embedded data

The engine does not use the globalization of .NET or of the operating system: it embeds its own copy of the data (see
[Intl, dates and time zones](08-IntlDatesAndTimeZones.md#the-same-results-on-every-machine)). This version of the
documentation describes this data:

| Data | Version | Used by |
|---|---|---|
| Unicode | 18.0.0 | the identifiers of the parser, the property escapes and the case-insensitive matching of `RegExp`, `toUpperCase`, `toLowerCase`, `normalize` |
| CLDR | 49.0.0 | `Intl`, the `toLocaleString` methods |
| Unicode Collation Algorithm | 18.0.0 | `Intl.Collator`, `localeCompare` |
| IANA time zone database | 2026f | `Date`, `Intl.DateTimeFormat`, `Temporal` |

- A test checks this table against the data of the library, so it changes when the data is updated. The data of a
  given version of the package is listed in `tools/Meziantou.Framework.JavaScriptEngine.Data.Generator/data-versions.json`
  of the repository, at the commit of that version.
- At run time, [UnicodeProperties.UnicodeVersion](cref:Meziantou.Framework.UnicodeProperties.UnicodeVersion) of `Meziantou.Framework.Unicode` gives the Unicode version.
- Node.js, the browsers and the operating systems each ship their own versions of the same data, updated at their own
  pace. A result that depends on it can differ from them even when both engines are correct: a character added in a
  new Unicode version matches `\p{L}` in one and not in the other, a locale gets a new date format, a country changes
  its time zone rules.

## Results that differ between engines

ECMAScript leaves some results to the implementation. They are not bugs, and they can differ between this engine,
Node.js and the browsers. Do not compare them byte for byte, and do not parse them:

- **Date strings.** `Date.prototype.toString` and `toTimeString` end with the name of the time zone, which the
  specification leaves to the implementation: the engine writes the abbreviation (`GMT-0500 (EST)`) where Node.js writes
  the long name (`GMT-0500 (Eastern Standard Time)`). Use `toISOString`, or `Intl.DateTimeFormat` with explicit
  options, for a stable format.
- **Error messages and stack traces.** The `message` of the errors thrown by the engine (`Cannot read properties of
  null (reading 'x')`) and the format of `stack` are the engine's own. Check the type of an error (`instanceof
  TypeError`, `name`), not its message.
- **Math.** `Math.sin`, `Math.tan`, `Math.exp`, `Math.pow`... are *implementation-approximated*: the engine uses the
  functions of .NET, which can differ from V8 in the last digit, and between operating systems. The operations of the
  language (`+`, `*`, `/`, `%`) and `Math.sqrt` are correctly rounded: they give the same results everywhere.
- **Intl.** The formats come from the CLDR data, and some choices are left to the engine (the `best fit` matching of
  the locales and of the date formats, the list of the locales, `Intl.supportedValuesOf`). The same call can give
  another string, or another number of results, in Node.js or in a browser, and after an update of the data.
- **Function source.** `Function.prototype.toString` of a built-in function is `function max() { [native code] }`;
  its exact text is the engine's own.

The failures of [test262](10-Conformance.md) come from the data too: the generated tests of the `RegExp` property
escapes expect the Unicode 17 data, and the engine uses Unicode 18. The expectation files of
`tests/Meziantou.Framework.JavaScriptEngine.Test262/expectations` give the reason of each known failure.
