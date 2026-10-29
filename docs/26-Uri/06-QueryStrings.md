---
title: Query strings
description: Parse a query string, and add, replace or remove the query string parameters of a URL.
---

`QueryStringUtilities` reads the parameters of a query string and returns new URLs with parameters added, replaced or
removed. The URLs are strings, absolute or relative, and the fragment (`#…`) is kept at the end.

## Parse a query string

`ParseQuery` returns a `QueryStringParameterCollection`: the parameters in the order of the query string, each with
its values as `StringValues`:

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/QueryStringUtilitiesTests.cs;region=QueryStringUtilities_ParseQuery;format=md-fenced-code-block;dedent=true -->
```csharp
// The leading "?" is optional, names and values are decoded
var query = QueryStringUtilities.ParseQuery("q=hello+world&path=%2Fdocs&tag=a&tag=b&debug");

Assert.Equal("hello world", query["q"].ToString());
Assert.Equal("/docs", query["path"].ToString());
Assert.Equal(["a", "b"], query["tag"].ToArray());

// A parameter without "=" has an empty value
Assert.Equal("", query["debug"].ToString());

// A missing parameter has no value
Assert.True(StringValues.IsNullOrEmpty(query["missing"]));

// Parameters are enumerated in the order of the query string
Assert.Equal(["q", "path", "tag", "debug"], query.Select(parameter => parameter.Key));

// ParseNullableQuery returns null instead of an empty collection
Assert.Null(QueryStringUtilities.ParseNullableQuery("?"));
Assert.True(QueryStringUtilities.ParseQuery("?").IsEmpty);
```
<!-- endref -->

- The leading `?` is optional, so you can pass `Uri.Query` as is.
- Names and values are percent-decoded, and `+` is decoded as a space.
- A name that appears several times has several values, in order. A name is compared with the exact case: `Tag` and
  `tag` are two parameters.
- A missing name returns `StringValues.Empty`.
- `ParseNullableQuery` returns `null` for an empty query string, where `ParseQuery` returns an empty collection.

## Add, replace and remove parameters

Each method takes a URL and returns a new one:

| Method | Effect |
| --- | --- |
| `AddQueryString` | Appends parameters after the existing ones, which stay as they are. |
| `AddOrReplaceQueryString` | Replaces every value of the parameters that exist, keeping their position, and appends the others. |
| `RemoveQueryString` | Removes a parameter and all its values. |
| `SetQueryString` | Replaces the whole query string. |

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/QueryStringUtilitiesTests.cs;region=QueryStringUtilities_EditQuery;format=md-fenced-code-block;dedent=true -->
```csharp
var uri = "https://example.com/search?q=books#results";

// Values are encoded, and the fragment stays at the end
Assert.Equal(
    "https://example.com/search?q=books&author=Jane%20Doe#results",
    QueryStringUtilities.AddQueryString(uri, "author", "Jane Doe"));

// Several parameters at once, a name with several values
Assert.Equal(
    "https://example.com/search?q=books&tag=a&tag=b&page=2#results",
    QueryStringUtilities.AddQueryString(uri, [("tag", new StringValues(["a", "b"])), ("page", new StringValues("2"))]));

// A null value adds nothing
Assert.Equal(uri, QueryStringUtilities.AddQueryString(uri, "author", null));

// AddOrReplaceQueryString replaces every value of the name, and a null value removes it
Assert.Equal("https://example.com/search?q=novels#results", QueryStringUtilities.AddOrReplaceQueryString(uri, "q", "novels"));
Assert.Equal("https://example.com/search#results", QueryStringUtilities.AddOrReplaceQueryString(uri, "q", null));

// The parameters that are kept are written again
Assert.Equal("https://example.com/?q=a%20b&debug=&page=2", QueryStringUtilities.AddOrReplaceQueryString("https://example.com/?q=a+b&debug", "page", "2"));
```
<!-- endref -->

- Names and values are percent-encoded; a space becomes `%20`.
- The overloads take one name and value, or several parameters as tuples, `KeyValuePair` values or a dictionary, with
  `string` or `StringValues` values.
- A `null` value adds nothing. With `AddOrReplaceQueryString`, it removes the parameter.
- `AddOrReplaceQueryString`, `RemoveQueryString` and `SetQueryString` write the whole query string again, so the
  parameters they keep are encoded again: `?q=a+b` becomes `?q=a%20b`, which has the same meaning, and `?debug` becomes
  `?debug=`.

## Edit a parsed query string

For several changes, edit the `QueryStringParameterCollection` and write it back with `SetQueryString`:

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/QueryStringUtilitiesTests.cs;region=QueryStringParameterCollection_Edit;format=md-fenced-code-block;dedent=true -->
```csharp
var uri = "https://example.com/search?q=books&page=3&utm_source=newsletter";

var query = QueryStringUtilities.ParseQuery(new System.Uri(uri).Query);
query.Remove("utm_source");
query.Set("page", "1");
query.Append("tag", "fiction");
query.Append("tag", "history");

Assert.Equal(
    "https://example.com/search?q=books&page=1&tag=fiction&tag=history",
    QueryStringUtilities.SetQueryString(uri, query));
```
<!-- endref -->

| Member | Effect |
| --- | --- |
| `Get(name)`, the indexer | The values of the parameter, or `StringValues.Empty`. |
| `Set(name, values)`, the indexer | Replaces the values of the parameter, or adds it at the end. |
| `Append(name, value)` | Adds values to the parameter, or adds it at the end. |
| `Remove(name)` | Removes the parameter and all its values. |
| `Clear()` | Removes every parameter. |
| `Count`, `IsEmpty` | The number of distinct parameter names. |

## Next steps

- [Matching URLs](03-MatchingUrls.md): match the search component of a URL with a pattern.
- [Limitations](08-Limitations.md): what the library does not do.
