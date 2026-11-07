---
title: Getting started
description: Install the package and create a slug from a text.
---

## Install the package

Add the `Meziantou.Framework.Slug` package to your project:

<InstallNuGetPackage Id="Meziantou.Framework.Slug" />

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework;`. They check
their results with assertions such as `Assert.Equal`, which show what each call returns.

## Create a slug

<!-- ref:../../tests/Meziantou.Framework.Slug.Tests/SlugTests.cs;region=Slug_Create;format=md-fenced-code-block;dedent=true -->
```csharp
var result = Slug.Create("This is a text");
Assert.Equal("This-is-a-text", result);
```
<!-- endref -->

The default rules remove diacritics and punctuation:

<!-- ref:../../tests/Meziantou.Framework.Slug.Tests/SlugTests.cs;region=Slug_DefaultRules;format=md-fenced-code-block;dedent=true -->
```csharp
// Diacritics are removed
Assert.Equal("Creme-brulee-a-la-francaise", Slug.Create("Crème brûlée à la française"));

// Other characters become a single separator, never at the start or the end
Assert.Equal("Hello-World-2024", Slug.Create("  Hello, World! (2024)  "));

// Text written outside the allowed ranges gives an empty slug
Assert.Equal("", Slug.Create("Привет, мир"));
```
<!-- endref -->

- `Slug.Create(null)` returns `null`.
- The result is an empty string when no character of the text is allowed. With the default options, this is the case
  for any text written without Latin letters or digits, such as a Cyrillic, Greek, Arabic or CJK title. Handle the empty
  slug before you put it in a URL or a file path, or [allow more characters](03-Options.md#allow-more-characters).
- Text that is not well-formed Unicode, such as a string cut in the middle of a surrogate pair, does not throw.

## Lower-case slugs

URLs are often in lower case. Set [CasingTransformation](cref:Meziantou.Framework.CasingTransformation):

<!-- ref:../../tests/Meziantou.Framework.Slug.Tests/SlugTests.cs;region=Slug_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var slug = Slug.Create("This is a test!");
Assert.Equal("This-is-a-test", slug);

var lowerSlug = Slug.Create("Hello World", new SlugOptions { CasingTransformation = CasingTransformation.ToLowerCase });
Assert.Equal("hello-world", lowerSlug);
```
<!-- endref -->

## Next steps

- [Options](03-Options.md): the maximum length, the separator, the allowed characters, and custom replacements.
