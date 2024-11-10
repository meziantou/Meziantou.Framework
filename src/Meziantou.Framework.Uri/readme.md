# Meziantou.Framework.Uri

`Meziantou.Framework.Uri` provides methods to manipulate URIs.

Parse and edit query strings

````c#
var uri = "https://www.meziantou.net";
var query = QueryStringUtilities.ParseQueryString(uri);
var author = query["author"];

_ = QueryStringUtilities.AddQueryString(uri, "name", "value");
_ = QueryStringUtilities.AddOrReplaceQueryString(uri, "name", "value");
_ = QueryStringUtilities.SetQueryString(uri, "name", "value");

// Remove a query string parameter
_ = QueryStringUtilities.AddQueryString(uri, "name", null);
````