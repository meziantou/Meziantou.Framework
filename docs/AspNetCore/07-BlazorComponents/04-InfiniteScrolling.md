---
title: Infinite scrolling
description: Load more items when the user reaches the end of a list, page through an API, reset the list, and use it in tables and scrollable containers.
---

[InfiniteScrolling\<T\>](cref:Meziantou.AspNetCore.Components.InfiniteScrolling{T}) asks for more items when the end of the list becomes visible:

<!-- ref:../../../samples/BlazorAppSample/Pages/InfiniteScrollingDemo.razor;format=md-fenced-code-block -->
```razor
@page "/infinite-scrolling"

<h1>Infinite scrolling!</h1>

<InfiniteScrolling ItemsProvider="GetItems">
    <ItemTemplate Context="item">
        <div>Item @item</div>
    </ItemTemplate>
    <LoadingTemplate>
        <div><span class="oi oi-reload spin"></span> Loading more items...</div>
    </LoadingTemplate>
</InfiniteScrolling>

@code {
    async Task<IEnumerable<int>> GetItems(InfiniteScrollingItemsProviderRequest request)
    {
        await Task.Delay(500);
        return Enumerable.Range(request.StartIndex, 10);
    }
}
```
<!-- endref -->

[ItemsProvider](cref:Meziantou.AspNetCore.Components.InfiniteScrolling{T}.ItemsProvider) receives an [InfiniteScrollingItemsProviderRequest](cref:Meziantou.AspNetCore.Components.InfiniteScrollingItemsProviderRequest) and returns the next items:

- [StartIndex](cref:Meziantou.AspNetCore.Components.InfiniteScrollingItemsProviderRequest.StartIndex) is the number of items already loaded, which is the index of the first item to return.
- [CancellationToken](cref:Meziantou.AspNetCore.Components.InfiniteScrollingItemsProviderRequest.CancellationToken) is canceled when the list is reset or the component is disposed. Pass it to your API calls.
- Return an empty sequence when there is nothing left: the component stops asking.

[LoadingTemplate](cref:Meziantou.AspNetCore.Components.InfiniteScrolling{T}.LoadingTemplate) is displayed while a request is running. Only one request runs at a time.

The component watches the end of the list with an `IntersectionObserver` in the browser, so it only works once the
component is interactive.

## Load pages from an API

Compute the page from [StartIndex](cref:Meziantou.AspNetCore.Components.InfiniteScrollingItemsProviderRequest.StartIndex):

<!-- ref:../../../samples/BlazorAppSample/Pages/InfiniteScrollingPaginationDemo.razor;format=md-fenced-code-block -->
```razor
@page "/infinite-scrolling-pagination"

<h1>Infinite scrolling!</h1>

<InfiniteScrolling ItemsProvider="GetItems">
    <ItemTemplate Context="item">
        <div>@item</div>
    </ItemTemplate>
    <LoadingTemplate>
        <div><span class="oi oi-reload spin"></span> Loading more items...</div>
    </LoadingTemplate>
</InfiniteScrolling>

@code {
    string[] AllItems = Enumerable.Range(0, 100).Select(i=> $"Item {i}").ToArray();

    async Task<IEnumerable<string>> GetItems(InfiniteScrollingItemsProviderRequest request)
    {
        const int PageSize = 20;
        var pageIndex = request.StartIndex / PageSize;

        // Simulate a call to an API or a DB to load a page
        await Task.Delay(500);
        return AllItems.Skip(pageIndex * PageSize).Take(PageSize).ToArray();
    }
}
```
<!-- endref -->

## Reset the list

Call [RefreshDataAsync](cref:Meziantou.AspNetCore.Components.InfiniteScrolling{T}.RefreshDataAsync) to discard the loaded items and start again from index 0, for instance when a search filter
changes. The running request is canceled:

<!-- ref:../../../samples/BlazorAppSample/Pages/InfiniteScrollingSearchDemo.razor;format=md-fenced-code-block -->
```razor
@page "/infinite-scrolling-search"
@using System.Globalization

<h1>Infinite scrolling!</h1>

<input type="number" value="@query" @oninput="QueryChanged" />

<InfiniteScrolling @ref="InfiniteScrollingInstance" ItemsProvider="request => GetItems(request)">
    <ItemTemplate Context="item">
        <div>Item @item</div>
    </ItemTemplate>
    <LoadingTemplate>
        <div><span class="oi oi-reload spin"></span> Loading more items...</div>
    </LoadingTemplate>
</InfiniteScrolling>

@code {
    InfiniteScrolling<int>? InfiniteScrollingInstance;
    string? query = "0";

    async Task<IEnumerable<int>> GetItems(InfiniteScrollingItemsProviderRequest request)
    {
        if (!int.TryParse(query, System.Globalization.NumberStyles.None, CultureInfo.InvariantCulture, out var minValue))
        {
            minValue = 0;
        }

        await Task.Delay(500);
        return Enumerable.Range(request.StartIndex + minValue, 10);
    }

    private async Task QueryChanged(ChangeEventArgs e)
    {
		query = (string?)e.Value;
        await InfiniteScrollingInstance!.RefreshDataAsync();
    }
}
```
<!-- endref -->

Assigning another delegate to [ItemsProvider](cref:Meziantou.AspNetCore.Components.InfiniteScrolling{T}.ItemsProvider) also clears the items.

## In a table or a scrollable container

The component renders an element after the last item to detect when the end is visible. It is a `<div>` by default;
set [LastItemIndicatorElement](cref:Meziantou.AspNetCore.Components.InfiniteScrolling{T}.LastItemIndicatorElement) when the items are in an element that only accepts some children, such as `tr` in a
`<table>`. The end of the list is detected in the closest ancestor that scrolls (its `overflow` is not `visible`), so
the component also works in a scrollable container, vertically or horizontally:

<!-- ref:../../../samples/BlazorAppSample/Pages/InfiniteScrollingContainersDemo.razor;format=md-fenced-code-block -->
```razor
@page "/infinite-scrolling-containers"

<h1>Infinite scrolling!</h1>

<h2>Vertical</h2>

<div style="overflow-y: scroll; max-height: 200px">
    <InfiniteScrolling ItemsProvider="GetItems">
        <ItemTemplate Context="item">
            <div>Item @item</div>
        </ItemTemplate>
        <LoadingTemplate>
            <div><span class="oi oi-reload spin"></span> Loading more items...</div>
        </LoadingTemplate>
    </InfiniteScrolling>
</div>

<h2>Horizontal</h2>

<div style="overflow-x: scroll; display: flex; flex-direction: row; max-width: 200px">
    <InfiniteScrolling ItemsProvider="GetItems">
        <ItemTemplate Context="item">
            <div>Item @item</div>
        </ItemTemplate>
        <LoadingTemplate>
            <div><span class="oi oi-reload spin"></span> Loading more items...</div>
        </LoadingTemplate>
    </InfiniteScrolling>
</div>

<h2>Table</h2>

<table style="overflow-y: scroll; max-height: 200px">
    <thead>
        <tr><th>Items</th></tr>
    </thead>
    <InfiniteScrolling ItemsProvider="GetItems" LastItemIndicatorElement="tr">
        <ItemTemplate Context="item">
            <tr><td>Item @item</td></tr>
        </ItemTemplate>
        <LoadingTemplate>
            <tr><td><span class="oi oi-reload spin"></span> Loading more items...</td></tr>
        </LoadingTemplate>
    </InfiniteScrolling>
</table>


@code {
    async Task<IEnumerable<int>> GetItems(InfiniteScrollingItemsProviderRequest request)
    {
        await Task.Delay(100);
        if (request.StartIndex > 50)
            return Enumerable.Empty<int>();

        return Enumerable.Range(request.StartIndex, 10);
    }
}
```
<!-- endref -->
