---
title: Query string
description: Fill component properties from the query string, write them back to the URL, and read query string values.
---

Keep the state of a page, such as a search or a page number, in the URL, so it can be bookmarked and shared.

## Synchronize component properties

`QueryStringService` reads and writes the properties that have both `[Parameter]` and `[SupplyParameterFromQuery]`.
Register it with `builder.Services.AddQueryStringParameters()`, then call `SetParametersFromQueryString` to fill the
properties and `UpdateQueryString` to write them to the URL:

<!-- ref:../../../samples/BlazorAppSample/Examples/QueryStringServiceExample.razor;format=md-fenced-code-block -->
```razor
@inject QueryStringService QueryStringService

<input type="search" value="@SearchTerm" @onchange="e => UpdateSearch(e.Value as string)" />

@code {
    [Parameter]
    [SupplyParameterFromQuery]
    public string? SearchTerm { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public int PageNumber { get; set; } = 1;

    protected override void OnInitialized()
    {
        // Read query string parameters and populate properties
        QueryStringService.SetParametersFromQueryString(this);
    }

    private async Task UpdateSearch(string? newSearchTerm)
    {
        SearchTerm = newSearchTerm;
        // Update the URL with new query string parameters
        await QueryStringService.UpdateQueryString(this);
    }
}
```
<!-- endref -->

- The name in the query string is the `Name` of `[SupplyParameterFromQuery]`, or the property name.
- `SetParametersFromQueryString` uses the first value of the parameter. The query string comes from the user: a value
  that cannot be converted to the type of the property leaves the property unchanged instead of throwing.
- `UpdateQueryString` writes the properties that are not `null` and removes the parameters of the properties that are.
  The other parameters of the URL are kept.

By default, `UpdateQueryString` navigates to the new URL: this is a client-side navigation, not a page reload, and the
router renders the page again. Pass `reloadPage: false` to change the URL in place with the history API of the
browser: nothing is rendered again, and no history entry is added.

## Update the URL from the NavigationManager

`NavigationManager.UpdateUrlUsingParameters` writes the same properties to the URL with
`NavigationManager.GetUriWithQueryParameters`, without a service to register:

<!-- ref:../../../samples/BlazorAppSample/Pages/QueryStringDemo.razor;format=md-fenced-code-block -->
```razor
@page "/querystring"
@inject NavigationManager NavigationManager

<h1>QueryString service!</h1>

<input type="text" @bind="Text" />
<button @onclick="UpdateQueryString">Update QueryString</button>
<button @onclick="UpdateQueryStringJS">Update QueryString without changing history</button>

@code {
    [Parameter, SupplyParameterFromQuery]
    public string? Text { get; set; }

    void UpdateQueryString()
    {
        NavigationManager.UpdateUrlUsingParameters(this, replaceHistory: false);
    }

    void UpdateQueryStringJS()
    {
        NavigationManager.UpdateUrlUsingParameters(this, replaceHistory: true);
    }
}
```
<!-- endref -->

It navigates only when the URL changes. Pass `replaceHistory: true` to replace the current history entry instead of
adding one. Values of types that `GetUriWithQueryParameters` does not support, such as enums or `TimeSpan`, are
written as strings.

## Read a value

`GetQueryStringParameterValue` returns the first value of a parameter, or `null`, and `GetQueryStringParameterValues`
returns all its values:

<!-- ref:../../../samples/BlazorAppSample/Documentation/QueryStringReadExample.razor;format=md-fenced-code-block -->
```razor
@inject NavigationManager NavigationManager

<p>Search: @search</p>
<p>Tags: @string.Join(", ", tags)</p>

@code {
    private string? search;
    private IReadOnlyList<string> tags = [];

    protected override void OnInitialized()
    {
        // For https://example.com/products?q=shoes&tag=red&tag=sale
        search = NavigationManager.GetQueryStringParameterValue("q");   // "shoes"
        tags = NavigationManager.GetQueryStringParameterValues("tag");  // ["red", "sale"]
    }
}
```
<!-- endref -->
