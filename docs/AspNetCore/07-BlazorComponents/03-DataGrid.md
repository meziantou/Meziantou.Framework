---
title: Data grid
description: Render a table from columns declared in markup, with formats, custom cells, row classes and row keys.
---

[DataGrid\<TRowData\>](cref:Meziantou.AspNetCore.Components.DataGrid{TRowData}) renders a `<table>` from the [DataGridColumn\<TRowData\>](cref:Meziantou.AspNetCore.Components.DataGridColumn{TRowData}) components it contains:

<!-- ref:../../../samples/BlazorAppSample/Pages/DataGridDemo.razor;format=md-fenced-code-block -->
```razor
@page "/datagrid"
@inject WeatherForecastClient WeatherForecastClient

<h1>DataGrid</h1>

<DataGrid Items="@data" class="table">
    <DataGridColumn TRowData="WeatherForecast" Expression="item=>item.Date" Format="D" />
    <DataGridColumn TRowData="WeatherForecast" Expression="item=>item.Summary" />
    <DataGridColumn TRowData="WeatherForecast" Expression="item=>item.TemperatureC" Title="Temp. (C)" />
    <DataGridColumn TRowData="WeatherForecast" Title="Temp. (F)">@context.TemperatureF</DataGridColumn>
</DataGrid>

@code {
    private IReadOnlyList<WeatherForecast>? data;

    protected override async Task OnInitializedAsync()
    {
        data = await WeatherForecastClient.GetAsync();
    }
}
```
<!-- endref -->

A column displays the value of its [Expression](cref:Meziantou.AspNetCore.Components.DataGridColumn{TRowData}.Expression), or its content when it has one; the content receives the row as
`context`. Columns must be inside a [DataGrid](cref:Meziantou.AspNetCore.Components.DataGrid{TRowData}): a column outside one throws an `InvalidOperationException`.

## Columns

| Parameter | Description |
| --- | --- |
| [Expression](cref:Meziantou.AspNetCore.Components.DataGridColumn{TRowData}.Expression) | The value of the cell, such as `item => item.Summary` |
| [Title](cref:Meziantou.AspNetCore.Components.DataGridColumn{TRowData}.Title) | The header. When it is not set, the name of the property of [Expression](cref:Meziantou.AspNetCore.Components.DataGridColumn{TRowData}.Expression) is used |
| [Format](cref:Meziantou.AspNetCore.Components.DataGridColumn{TRowData}.Format) | A .NET format string applied to the value, such as `D` for a date or `0.00` for a number |
| [ChildContent](cref:Meziantou.AspNetCore.Components.DataGridColumn{TRowData}.ChildContent) | Custom cell content. It replaces [Expression](cref:Meziantou.AspNetCore.Components.DataGridColumn{TRowData}.Expression) for the cell; set [Title](cref:Meziantou.AspNetCore.Components.DataGridColumn{TRowData}.Title) for the header |

The title can only be inferred from a member access, such as `item => item.Date`. For any other expression, set
[Title](cref:Meziantou.AspNetCore.Components.DataGridColumn{TRowData}.Title), or rendering throws a `NotSupportedException`.

`TRowData` must be set on each column, because a column cannot infer it from the grid.

## Rows

<!-- ref:../../../samples/BlazorAppSample/Documentation/DataGridRowsExample.razor;format=md-fenced-code-block -->
```razor
<DataGrid Items="@orders" class="table" RowKey="@(order => order.Id)" RowClass="@GetRowClass">
    <DataGridColumn TRowData="Order" Expression="order => order.Id" Title="#" />
    <DataGridColumn TRowData="Order" Expression="order => order.Customer" />
    <DataGridColumn TRowData="Order" Expression="order => order.Total" Format="0.00" />
    <DataGridColumn TRowData="Order" Title="Status">
        @(context.IsLate ? "Late" : "On time")
    </DataGridColumn>
</DataGrid>

@code {
    private readonly List<Order> orders =
    [
        new Order(1, "Alice", 12.5m, IsLate: false),
        new Order(2, "Bob", 99m, IsLate: true),
    ];

    private static string GetRowClass(Order order, int rowIndex)
    {
        if (order.IsLate)
            return "table-warning";

        return rowIndex % 2 == 0 ? "even" : "odd";
    }

    private sealed record Order(int Id, string Customer, decimal Total, bool IsLate);
}
```
<!-- endref -->

- **[RowClass](cref:Meziantou.AspNetCore.Components.DataGrid{TRowData}.RowClass)** returns the CSS class of a row from the item and its index.
- **[RowKey](cref:Meziantou.AspNetCore.Components.DataGrid{TRowData}.RowKey)** returns a stable, unique key for each row, used as the `@key` of the `<tr>`. With a key, Blazor keeps the
  right row elements when items are inserted, removed or reordered. Without it, the row index is used. Keys must be
  unique: a duplicate key makes the renderer throw.
- Any other attribute, such as `class`, is applied to the `<table>` element.

Columns can be added or removed conditionally (`@if` around a column): the grid follows.
