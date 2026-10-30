---
title: Repeater and loading indicator
description: Render a list with item, separator, empty and loading templates, and show a placeholder while data loads.
---

## Render a list

[Repeater\<T\>](cref:Meziantou.AspNetCore.Components.Repeater{T}) renders [ItemTemplate](cref:Meziantou.AspNetCore.Components.Repeater{T}.ItemTemplate) for each item of [Items](cref:Meziantou.AspNetCore.Components.Repeater{T}.Items), with [ItemSeparatorTemplate](cref:Meziantou.AspNetCore.Components.Repeater{T}.ItemSeparatorTemplate) between two items:

<!-- ref:../../../samples/BlazorAppSample/ReadmeExamples/RepeaterExample.razor;format=md-fenced-code-block -->
```razor
<Repeater Items="items">
    <ItemTemplate Context="item">
        <span>@item</span>
    </ItemTemplate>
    <ItemSeparatorTemplate>, </ItemSeparatorTemplate>
    <EmptyTemplate><em>No item</em></EmptyTemplate>
    <LoadingTemplate><em>Loading...</em></LoadingTemplate>
</Repeater>

@code {
    private List<string>? items = new() { "A", "B", "C" };
}
```
<!-- endref -->

`T` is inferred from [Items](cref:Meziantou.AspNetCore.Components.Repeater{T}.Items). Set it explicitly (`<Repeater T="Person">`) when the compiler cannot infer it.

| Parameter | Rendered when |
| --- | --- |
| [LoadingTemplate](cref:Meziantou.AspNetCore.Components.Repeater{T}.LoadingTemplate) | [Items](cref:Meziantou.AspNetCore.Components.Repeater{T}.Items) is `null`: the data is not loaded yet |
| [EmptyTemplate](cref:Meziantou.AspNetCore.Components.Repeater{T}.EmptyTemplate) | [Items](cref:Meziantou.AspNetCore.Components.Repeater{T}.Items) is empty. It replaces the container and the items |
| [ItemTemplate](cref:Meziantou.AspNetCore.Components.Repeater{T}.ItemTemplate) | For each item; the item is the `context` (rename it with `Context="..."`) |
| [ItemSeparatorTemplate](cref:Meziantou.AspNetCore.Components.Repeater{T}.ItemSeparatorTemplate) | Between two items |
| [RepeaterContainerTemplate](cref:Meziantou.AspNetCore.Components.Repeater{T}.RepeaterContainerTemplate) | Around all the items. By default, the items are rendered without a container |

Keep [Items](cref:Meziantou.AspNetCore.Components.Repeater{T}.Items) `null` until the data is loaded, then assign it: the repeater switches from the loading template to the
items or to the empty template.

[Items](cref:Meziantou.AspNetCore.Components.Repeater{T}.Items) is enumerated once per render. A lazy sequence, such as a LINQ query, is materialized into a list first, so its
work does not run twice.

## Wrap the items in a container

[RepeaterContainerTemplate](cref:Meziantou.AspNetCore.Components.Repeater{T}.RepeaterContainerTemplate) receives the rendered items as its context. Use it for markup that must only appear when
there are items, such as a table:

<!-- ref:../../../samples/BlazorAppSample/Pages/RepeaterDemo.razor;format=md-fenced-code-block -->
```razor
@page "/repeater"
@inject WeatherForecastClient WeatherForecastClient

<h1>Repeater</h1>

<Repeater Items="@data">

    <LoadingTemplate>
        <p><em>Loading...</em></p>
    </LoadingTemplate>

    <RepeaterContainerTemplate Context="ItemsContent">
        <table class="table">
            <thead>
                <tr>
                    <th>Date</th>
                    <th>Temp. (C)</th>
                    <th>Summary</th>
                </tr>
            </thead>
            <tbody>
                @ItemsContent
            </tbody>
        </table>
    </RepeaterContainerTemplate>

    <ItemTemplate Context="forecast">
        <tr>
            <td>@forecast.Date.ToShortDateString()</td>
            <td>@forecast.TemperatureC</td>
            <td>@forecast.Summary</td>
        </tr>
    </ItemTemplate>

</Repeater>

@code {
    private IReadOnlyList<WeatherForecast>? data;

    protected override async Task OnInitializedAsync()
    {
        data = await WeatherForecastClient.GetAsync();
    }
}
```
<!-- endref -->

When the list is empty, the [EmptyTemplate](cref:Meziantou.AspNetCore.Components.Repeater{T}.EmptyTemplate) replaces the whole container, so there is no table with an empty body.
Without an [EmptyTemplate](cref:Meziantou.AspNetCore.Components.Repeater{T}.EmptyTemplate), an empty list renders the container with no items.

## Show a loading indicator

[LoadingIndicator](cref:Meziantou.AspNetCore.Components.LoadingIndicator) renders [LoadingTemplate](cref:Meziantou.AspNetCore.Components.LoadingIndicator.LoadingTemplate) while [IsLoading](cref:Meziantou.AspNetCore.Components.LoadingIndicator.IsLoading) is `true`, and its content otherwise:

<!-- ref:../../../samples/BlazorAppSample/Documentation/LoadingIndicatorExample.razor;format=md-fenced-code-block -->
```razor
<LoadingIndicator IsLoading="@(forecasts is null)">
    <LoadingTemplate>
        <p><em>Loading the forecasts...</em></p>
    </LoadingTemplate>
    <ChildContent>
        <p>@forecasts!.Count forecasts</p>
    </ChildContent>
</LoadingIndicator>

@code {
    private List<string>? forecasts;

    protected override async Task OnInitializedAsync()
    {
        await Task.Delay(500); // Simulate a call to an API
        forecasts = ["Sunny", "Cloudy"];
    }
}
```
<!-- endref -->

Without a [LoadingTemplate](cref:Meziantou.AspNetCore.Components.LoadingIndicator.LoadingTemplate), it renders [LoadingText](cref:Meziantou.AspNetCore.Components.LoadingIndicator.LoadingText) (`Loading...` by default) in a `<div>`.
