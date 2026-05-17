# Meziantou.AspNetCore.Components

A collection of reusable Blazor components and services for common UI and browser-integration scenarios.

## Features

- **UI components**: `DataGrid`, `Repeater`, `GenericForm`, `LoadingIndicator`, `AnchorNavigation`, `InfiniteScrolling`
- **Input components**: `InputDateTime<TValue>`, `InputEnumSelect<TEnum>`, `InputGuid<TValue>`, `InputUrl<TValue>`
- **Browser services**: `ClipboardService`, `QueryStringService`, `TimeZoneService`

## Installation

```bash
dotnet add package Meziantou.AspNetCore.Components
```

## Usage

Add the package namespace in your app:

```razor
@using Meziantou.AspNetCore.Components
```

Register optional services:

```csharp
using Meziantou.AspNetCore.Components;

builder.Services.AddClipboard();
builder.Services.AddQueryStringParameters();
builder.Services.AddTimeZoneServices();
```

### Example: Repeater component

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

### Example: Query string synchronization

```razor
@inject QueryStringService QueryStringService

@code {
    [Parameter]
    [SupplyParameterFromQuery]
    public string? Search { get; set; }

    protected override void OnInitialized()
    {
        QueryStringService.SetParametersFromQueryString(this);
    }
}
```

## Additional resources

- [Anchor navigation in a Blazor application](https://www.meziantou.net/anchor-navigation-in-a-blazor-application.htm)
- [Copying text to clipboard in a Blazor application](https://www.meziantou.net/copying-text-to-clipboard-in-a-blazor-application.htm)
