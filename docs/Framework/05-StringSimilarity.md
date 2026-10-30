---
title: String similarity
description: Measure how different two strings are, find words that sound alike, and compute unique short names.
---

## Edit distance

<!-- ref:../../tests/Meziantou.Framework.Tests/StringSearchUtilitiesTests.cs;region=StringSearchUtilities_Levenshtein;format=md-fenced-code-block;dedent=true -->
```csharp
// The number of insertions, deletions and substitutions that turn one word into the other
Assert.Equal(3, StringSearchUtilities.Levenshtein("kitten", "sitting"));

// With a maximum distance, the computation stops early and returns maxDistance + 1
Assert.Equal(2, StringSearchUtilities.Levenshtein("kitten", "sitting", maxDistance: 1));

// Compare one word with many candidates
var commands = new[] { "build", "test", "publish" };
Assert.Equal([1, 4, 5], StringSearchUtilities.LevenshteinBatch("buid", commands));
```
<!-- endref -->

- [Levenshtein](cref:Meziantou.Framework.StringSearchUtilities.Levenshtein) returns the number of single-character insertions, deletions and substitutions needed to turn one
  string into the other. Use it to suggest the closest command or option when the user makes a typo.
- With `maxDistance`, the computation stops as soon as the distance is known to be greater, and returns
  `maxDistance + 1`. Pass it when you only care about close matches.
- [LevenshteinBatch](cref:Meziantou.Framework.StringSearchUtilities.LevenshteinBatch) compares a word with many candidates, in parallel, and returns one distance per candidate.
  `maxDegreeOfParallelism` limits the number of threads (`-1`, the default, lets the runtime decide).

## Hamming distance

<!-- ref:../../tests/Meziantou.Framework.Tests/StringSearchUtilitiesTests.cs;region=StringSearchUtilities_Hamming;format=md-fenced-code-block;dedent=true -->
```csharp
// The number of positions at which two strings of the same length differ
Assert.Equal(3, StringSearchUtilities.Hamming("karolin", "kathrin"));

// The number of different bits of two integers
Assert.Equal(2u, StringSearchUtilities.Hamming(0b1010u, 0b0110u));
```
<!-- endref -->

[Hamming](cref:Meziantou.Framework.StringSearchUtilities.Hamming) counts the positions where two sequences of the same length differ: strings, any sequences, or the bits of two
`uint` values. Sequences of different lengths throw an `ArgumentException`.

## Phonetic codes

<!-- ref:../../tests/Meziantou.Framework.Tests/StringSearchUtilitiesTests.cs;region=StringSearchUtilities_Soundex;format=md-fenced-code-block;dedent=true -->
```csharp
// Names that sound alike get the same code
Assert.Equal("R163", StringSearchUtilities.SoundexEnglish("Robert"));
Assert.Equal("R163", StringSearchUtilities.SoundexEnglish("Rupert"));
Assert.Equal(StringSearchUtilities.Metaphone("Smith"), StringSearchUtilities.Metaphone("Smyth"));
```
<!-- endref -->

Phonetic algorithms give the same code to words that sound alike, to find names despite spelling variations:

| Method | Algorithm |
| --- | --- |
| [SoundexEnglish](cref:Meziantou.Framework.StringSearchUtilities.SoundexEnglish) | The English Soundex |
| [SoundexFrench](cref:Meziantou.Framework.StringSearchUtilities.SoundexFrench) | A Soundex adapted to French |
| [Soundex2](cref:Meziantou.Framework.StringSearchUtilities.Soundex2) | An improved French Soundex |
| [Metaphone](cref:Meziantou.Framework.StringSearchUtilities.Metaphone) | Metaphone, for English |
| [Soundex](cref:Meziantou.Framework.StringSearchUtilities.Soundex) | A Soundex with your own table of letter codes and replacements |

Store the code next to the name, and search by code.

## Short names

[ShortName.Create](cref:Meziantou.Framework.ShortName.Create) computes short names that are unique within a collection, for instance to name columns or files with
a length limit:

<!-- ref:../../tests/Meziantou.Framework.Tests/ShortNameUtilitiesTests.cs;region=ShortName_Create;format=md-fenced-code-block;dedent=true -->
```csharp
// Create short names for a collection
var names = new[] { "CustomerName", "CustomerNumber", "CustomerAddress" };
var shortNames = ShortName.Create(names, maxLength: 5, StringComparer.OrdinalIgnoreCase);
Assert.Equal("Custo", shortNames["CustomerName"]);
Assert.Equal("Custm", shortNames["CustomerNumber"]);
Assert.Equal("Custe", shortNames["CustomerAddress"]);
```
<!-- endref -->

The other overloads create one short name that does not collide with a set of existing short names, and return `null`
when no unique short name fits in the maximum length.
