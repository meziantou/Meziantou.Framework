# Meziantou.Framework.Slug

Generate a slug from a string.

<!-- ref:../../tests/Meziantou.Framework.Slug.Tests/SlugTests.cs;region=Slug_Create;format=md-fenced-code-block;dedent=true -->
```csharp
var result = Slug.Create("This is a text");
Assert.Equal("This-is-a-text", result);
```
<!-- endref -->

You can customize the slug generation (`UnicodeRange` is in the `System.Text.Unicode` namespace):

<!-- ref:../../tests/Meziantou.Framework.Slug.Tests/SlugTests.cs;region=Slug_CustomOptions;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new SlugOptions
{
    MaximumLength = 20,
    Separator = "-",
    CanEndWithSeparator = false,
    CasingTransformation = CasingTransformation.ToLowerCase,
};

// AllowedRanges is read-only, so change the list in place instead of assigning a new one.
// It already contains a-z, A-Z and 0-9, so clear it first to replace that set.
options.AllowedRanges.Clear();
options.AllowedRanges.Add(UnicodeRange.Create('a', 'z'));
options.AllowedRanges.Add(UnicodeRange.Create('A', 'Z'));
options.AllowedRanges.Add(UnicodeRange.Create('0', '9'));

var result = Slug.Create("This is a text", options);
Assert.Equal("this-is-a-text", result);
```
<!-- endref -->

Note that an **empty** `AllowedRanges` allows every character instead of none, so clearing the list without
adding a range returns the text unfiltered.

`Slug.Create` returns `null` for `null`, and an empty string when no character of the input is allowed - which
is the case for any text written outside the default ranges, such as a Cyrillic or CJK title.