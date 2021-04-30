# Meziantou.Framework.Slug

Generate a slug from a string.

````c#
var result = Slug.Create("This is a text");
````

You can customize the slug generation:

````c#
var options = new SlugOptions()
{
    MaximumLength = 20,
    Separator = "-",
    CanEndWithSeparator = false,
    CasingTransformation = CasingTransformation.ToLowerCase,
    AllowedRanges = new List<UnicodeRange>
    {
        UnicodeRange.Create('a', 'z'),
        UnicodeRange.Create('A', 'Z'),
        UnicodeRange.Create('0', '9'),
    },
};
Slug.Create("This is a text", options); // this-is-a-text
````