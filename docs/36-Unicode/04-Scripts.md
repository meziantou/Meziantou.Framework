---
title: Scripts
description: Find the script of a character, the scripts a shared character is used with, and split a text into runs of one script.
---

## Get the script of a character

`UnicodeScripts` gives the script of a code point (the `Script` property of the standard), the scripts a shared
character is used with (`Script_Extensions`), and the names of the scripts:

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_Scripts;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Equal(UnicodeScript.Latin, UnicodeScripts.GetScript(new Rune('A')));
Assert.Equal(UnicodeScript.Cyrillic, UnicodeScripts.GetScript(new Rune(0x0410)));
Assert.Equal("Latn", UnicodeScripts.GetIso15924Code(UnicodeScript.Latin));

// Script_Extensions: the scripts a shared character is used with
Span<UnicodeScript> scripts = stackalloc UnicodeScript[UnicodeScripts.MaxScriptExtensionCount];
var count = UnicodeScripts.GetScriptExtensions(0x30FC, scripts);
Assert.Equal<UnicodeScript>([UnicodeScript.Hiragana, UnicodeScript.Katakana], scripts[..count]);
Assert.True(UnicodeScripts.HasScriptExtension(0x30FC, UnicodeScript.Katakana));

// The names of PropertyValueAliases.txt
Assert.True(UnicodeScripts.TryParseScript("Grek", out var script));
Assert.Equal(UnicodeScript.Greek, script);
```
<!-- endref -->

- Digits, punctuation and spaces are of the `Common` script, and combining marks of the `Inherited` script: they are
  used with many scripts. `Script_Extensions` narrows them down when the standard knows which scripts use them, such as
  the prolonged sound mark of Japanese, used with Hiragana and Katakana.
- `GetIso15924Code` gives the four-letter code of ISO 15924 (`Latn`), and `TryParseScript` accepts these codes and the
  names of the standard (`Greek`).

## Split a text into runs of one script

A font or a shaping engine works on runs of one script. `Unicode.EnumerateScriptRuns` splits a text into such runs, with
the rules of [UAX #24](https://www.unicode.org/reports/tr24/):

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_ScriptRuns;format=md-fenced-code-block;dedent=true -->
```csharp
var text = "gamma (\u03B3) is";
var runs = new List<string>();
foreach (UnicodeScriptRun run in Unicode.EnumerateScriptRuns(text))
{
    runs.Add($"{text.Substring(run.Index, run.Length)}: {run.Script}");
}

Assert.Equal(["gamma (: Latin", "\u03B3: Greek", ") is: Latin"], runs);
```
<!-- endref -->

- Combining marks follow their base, and spaces and punctuation follow the text before them.
- `Script_Extensions` are honored, and both brackets of a pair are in the same run.
- Each `UnicodeScriptRun` has the `Index` and the `Length` of the run in the text, and its `Script`.

To find out whether a user name mixes scripts in a suspicious way, use `Unicode.IsMixedScript`: see
[Look-alike characters](13-LookAlikeCharacters.md#detect-mixed-scripts).
