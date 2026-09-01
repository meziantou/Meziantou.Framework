# Meziantou.Framework.Slug

Generate a slug from a string.

````c#
var result = Slug.Create("This is a text");
````

You can customize the slug generation:

````c#
using System.Text.Unicode;

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

Slug.Create("This is a text", options); // this-is-a-text
````

Note that an **empty** `AllowedRanges` allows every character instead of none, so clearing the list without
adding a range returns the text unfiltered.

`Slug.Create` returns `null` for `null`, and an empty string when no character of the input is allowed - which
is the case for any text written outside the default ranges, such as a Cyrillic or CJK title.