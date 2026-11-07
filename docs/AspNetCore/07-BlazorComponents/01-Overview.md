---
title: Overview
description: Install Meziantou.AspNetCore.Components, import its namespace, register its services, and pick a component.
---

`Meziantou.AspNetCore.Components` is a Razor class library of Blazor components and browser services.

<InstallNuGetPackage Id="Meziantou.AspNetCore.Components" />

Import the namespace in `_Imports.razor`:

<!-- ref:../../../samples/BlazorAppSample/ReadmeExamples/_Imports.razor;format=md-fenced-code-block -->
```razor
@using Meziantou.AspNetCore.Components
```
<!-- endref -->

The components need no registration. The services do; register the ones you use (the extension methods are in the
[Meziantou.AspNetCore.Components](cref:Meziantou.AspNetCore.Components!N:Meziantou.AspNetCore.Components) namespace):

<!-- ref:../../../samples/BlazorAppSample/ReadmeExamples/ComponentsReadmeExamples.cs;region=Components_RegisterServices;format=md-fenced-code-block;dedent=true -->
```csharp
builder.Services.AddClipboard();
builder.Services.AddQueryStringParameters();
builder.Services.AddTimeZoneServices();
```
<!-- endref -->

The services are registered as scoped: one instance per user circuit in Blazor Server, one per application in Blazor
WebAssembly.

## Components

| Component | Use it to | Page |
| --- | --- | --- |
| [Repeater\<T\>](cref:Meziantou.AspNetCore.Components.Repeater{T}) | Render a template per item, with separator, empty and loading templates | [Repeater and loading indicator](02-RepeaterAndLoadingIndicator.md) |
| [LoadingIndicator](cref:Meziantou.AspNetCore.Components.LoadingIndicator) | Show a placeholder while data loads | [Repeater and loading indicator](02-RepeaterAndLoadingIndicator.md) |
| [DataGrid\<TRowData\>](cref:Meziantou.AspNetCore.Components.DataGrid{TRowData}) | Render a table from columns declared in markup | [Data grid](03-DataGrid.md) |
| [InfiniteScrolling\<T\>](cref:Meziantou.AspNetCore.Components.InfiniteScrolling{T}) | Load more items when the user scrolls to the end of a list | [Infinite scrolling](04-InfiniteScrolling.md) |
| [GenericForm\<TModel\>](cref:Meziantou.AspNetCore.Components.GenericForm{TModel}) | Generate the editors of a form from the properties of a model | [Generated forms](05-GenericForm.md) |
| [InputDateTime\<TValue\>](cref:Meziantou.AspNetCore.Components.InputDateTime{TValue}), [InputEnumSelect\<TEnum\>](cref:Meziantou.AspNetCore.Components.InputEnumSelect{TEnum}), [InputGuid\<TValue\>](cref:Meziantou.AspNetCore.Components.InputGuid{TValue}), [InputUrl\<TValue\>](cref:Meziantou.AspNetCore.Components.InputUrl{TValue}) | Edit dates with a time, enums, GUIDs and URLs in an `EditForm` | [Input components](06-InputComponents.md) |
| [AnchorNavigation](cref:Meziantou.AspNetCore.Components.AnchorNavigation) | Scroll to the element named by the `#fragment` of the URL | [Anchor navigation](10-AnchorNavigation.md) |

The [LogViewer](cref:Meziantou.AspNetCore.Components.LogViewer) component, which displays log entries, comes in its own package,
`Meziantou.AspNetCore.Components.LogViewer`; see [Log viewer](11-LogViewer/01-Overview.md).

## Services

| Service | Registration | Use it to | Page |
| --- | --- | --- | --- |
| [QueryStringService](cref:Meziantou.AspNetCore.Components.QueryStringService) | [AddQueryStringParameters()](cref:Meziantou.AspNetCore.Components.QueryStringServiceExtensions.AddQueryStringParameters) | Read and write component properties from the query string | [Query string](07-QueryString.md) |
| [ClipboardService](cref:Meziantou.AspNetCore.Components.ClipboardService) | [AddClipboard()](cref:Meziantou.AspNetCore.Components.ClipboardServiceExtensions.AddClipboard) | Read and write text on the clipboard | [Clipboard](08-Clipboard.md) |
| [TimeZoneService](cref:Meziantou.AspNetCore.Components.TimeZoneService) | [AddTimeZoneServices()](cref:Meziantou.AspNetCore.Components.TimeZoneServiceExtensions.AddTimeZoneServices) | Get the user's time zone and convert dates | [User time zone](09-TimeZones.md) |

`NavigationManager` also gets extension methods to read query string values and update the URL; see
[Query string](07-QueryString.md).

## JavaScript and render modes

[AnchorNavigation](cref:Meziantou.AspNetCore.Components.AnchorNavigation), [InfiniteScrolling](cref:Meziantou.AspNetCore.Components.InfiniteScrolling{T}), [ClipboardService](cref:Meziantou.AspNetCore.Components.ClipboardService) and [TimeZoneService](cref:Meziantou.AspNetCore.Components.TimeZoneService) call JavaScript. The scripts are
static web assets of the package, under `_content/Meziantou.AspNetCore.Components/`, loaded on demand: there is no
`<script>` tag to add, but the application must serve static web assets, as every Blazor application does.

JavaScript calls are only possible once the component is interactive, not during prerendering or static server-side
rendering. Call these services from event handlers or from `OnAfterRenderAsync`. The other components
([Repeater](cref:Meziantou.AspNetCore.Components.Repeater{T}), [LoadingIndicator](cref:Meziantou.AspNetCore.Components.LoadingIndicator), [DataGrid](cref:Meziantou.AspNetCore.Components.DataGrid{TRowData}), [GenericForm](cref:Meziantou.AspNetCore.Components.GenericForm{TModel}) and the inputs) also render with static server-side
rendering.
