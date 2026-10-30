---
title: Look-alike characters
description: Detect user names and domain names that imitate others with characters that look alike, or that mix writing systems.
---

Many characters look like others: the Cyrillic `а` looks like the Latin `a`. An attacker can register `раураl`,
written with Cyrillic letters, to pass for `paypal`. [UTS #39](https://unicode.org/reports/tr39/) defines how to detect
such names, and [Unicode](cref:Meziantou.Framework.Unicode) implements it.

## Detect names that look alike

[Unicode.AreConfusable](cref:Meziantou.Framework.Unicode.AreConfusable) tells whether two strings look alike:

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_AreConfusable;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.True(Unicode.AreConfusable("paypal", "\u0440\u0430\u0443\u0440\u0430l")); // Cyrillic look-alikes
Assert.True(Unicode.AreConfusable("caf\u00E9", "cafe\u0301")); // composed vs decomposed
Assert.False(Unicode.AreConfusable("paypal", "example"));
```
<!-- endref -->

To find the collisions in a set of names, such as the user names of an application, store the skeleton of each name
and look new names up by their skeleton. [Unicode.GetConfusableSkeleton](cref:Meziantou.Framework.Unicode.GetConfusableSkeleton) gives it:

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_GetConfusableSkeleton;format=md-fenced-code-block;dedent=true -->
```csharp
var userName = "\u0440\u0430\u0443\u0440\u0430l";
var skeleton = Unicode.GetConfusableSkeleton(userName);

Assert.Equal(Unicode.GetConfusableSkeleton("paypal"), skeleton);
```
<!-- endref -->

- The skeleton is a comparison key, not text: do not show it to users, and do not store it in place of the name.
- Two strings are confusable when their skeletons are equal. The comparison does not consider the scripts: combine it
  with the detection of mixed scripts below.
- The skeleton uses the normalization of this package, not the one of the platform: it is the same everywhere,
  including in globalization-invariant mode.
- A string with an unpaired surrogate throws an `ArgumentException`.

## Detect mixed scripts

A spoofed name usually mixes writing systems. [Unicode.IsMixedScript](cref:Meziantou.Framework.Unicode.IsMixedScript) applies the resolved script set rules of UTS #39,
and [Unicode.IsSingleScript](cref:Meziantou.Framework.Unicode.IsSingleScript) is its opposite:

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_IsMixedScript;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.False(Unicode.IsMixedScript("paypal")); // all Latin
Assert.True(Unicode.IsMixedScript("\u0440\u0430\u0443\u0440\u0430l")); // Cyrillic letters with a Latin "l"
Assert.False(Unicode.IsMixedScript("日本語です")); // Japanese legitimately mixes Han and Hiragana
Assert.False(Unicode.IsMixedScript("user123")); // digits match any script
```
<!-- endref -->

- Digits, punctuation and combining marks match any script: they never make a string mixed on their own.
- Japanese and Korean text, which mixes Han with Hiragana, Katakana or Hangul, is not mixed.
- The empty string is single-script.

## Replace look-alike characters

[Unicode.ReplaceConfusablesCharacters](cref:Meziantou.Framework.Unicode.ReplaceConfusablesCharacters) replaces each character that has a look-alike by its prototype, and
[Unicode.IsConfusableCharacter](cref:Meziantou.Framework.Unicode.IsConfusableCharacter) tells whether a character has one:

<!-- ref:../../tests/Meziantou.Framework.Unicode.Tests/UnicodeTests.cs;region=Unicode_ReplaceConfusablesCharacters;format=md-fenced-code-block;dedent=true -->
```csharp
var input = "\u0440\u0430\u0443\u0440\u0430l"; // "раураl": Cyrillic letters that look like Latin ones
var normalized = Unicode.ReplaceConfusablesCharacters(input);

Assert.Equal("paypal", normalized);
```
<!-- endref -->

To compare strings, prefer [AreConfusable](cref:Meziantou.Framework.Unicode.AreConfusable): it also normalizes the strings, so that a composed and a decomposed accent
compare equal.
