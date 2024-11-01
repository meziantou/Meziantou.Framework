using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace Meziantou.Framework.Internals;

internal sealed class QueryStringMetadata
{
    public QueryStringMetadata(string queryString)
    {
        QueryString = queryString;
        Query = QueryHelpers.ParseQuery(queryString);
    }

    public string QueryString { get; }
    public Dictionary<string, StringValues> Query { get; }
}
