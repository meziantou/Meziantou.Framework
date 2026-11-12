---
title: Calling the tools of a page (WebMCP)
description: List the tools that a page registers with WebMCP, and execute them from .NET as the agent of a browser does.
---

[WebMCP](https://webmachinelearning.github.io/webmcp/) lets a page expose its features as tools: JavaScript functions
with a name, a natural language description and a JSON Schema of their input, registered with
`document.modelContext.registerTool()`. An agent (for instance an AI model that drives the page) reads the tools and calls
them instead of clicking through the page. The application plays the role of the agent: it lists the tools of a browsing
context and executes them with JSON arguments.

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/ModelContextTests.cs;region=WebPlatform_WebMcp;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JavaScriptScriptEngine();
using var context = new BrowsingContext(new BrowsingContextOptions { ScriptEngine = engine });
await context.NavigateAsync("https://example.com/", _ => """
    <script>
      document.modelContext.registerTool({
        name: 'add',
        description: 'Adds two numbers',
        inputSchema: { type: 'object', properties: { a: { type: 'number' }, b: { type: 'number' } }, required: ['a', 'b'] },
        annotations: { readOnlyHint: true },
        execute: ({ a, b }) => ({ sum: a + b }),
      });
    </script>
    """);
await context.EventLoop.RunUntilIdleAsync();

// The tools registered by the page and by its iframes
var tool = Assert.Single(context.GetModelContextTools());
Assert.Equal("add", tool.Name);
Assert.Equal("Adds two numbers", tool.Description);
Assert.Equal("""{"type":"object","properties":{"a":{"type":"number"},"b":{"type":"number"}},"required":["a","b"]}""", tool.InputSchemaJson);
Assert.Equal(true, tool.ToolAnnotations?.ReadOnlyHint);
Assert.Equal("https://example.com", tool.Origin);

// The arguments and the result are JSON texts. Run the event loop until the tool completes.
var execution = context.ExecuteModelContextToolAsync(tool, """{"a":1,"b":2}""");
await context.EventLoop.RunUntilIdleAsync();
Assert.Equal("""{"sum":3}""", await execution);
```
<!-- endref -->

## Listing the tools

[BrowsingContext.GetModelContextTools](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContext.GetModelContextTools)
returns the tools registered by the active document of the browsing context and by the documents of its iframes, in tree
order, then in the order of their registration. Each [Tool](cref:Meziantou.Framework.WebPlatform.Dom.Tool) is a snapshot:
its name, title, description, the JSON of its input and output schemas
([InputSchemaJson](cref:Meziantou.Framework.WebPlatform.Dom.Tool.InputSchemaJson) and
[OutputSchemaJson](cref:Meziantou.Framework.WebPlatform.Dom.Tool.OutputSchemaJson), null when the page gave none), its
[ToolAnnotations](cref:Meziantou.Framework.WebPlatform.Dom.Tool.ToolAnnotations) (`readOnlyHint`, `consequentialHint`...),
and the [Document](cref:Meziantou.Framework.WebPlatform.Dom.Tool.Document) and
[Origin](cref:Meziantou.Framework.WebPlatform.Dom.Tool.Origin) that registered it. The application sees the tools of every
origin of the frame tree: the `exposedTo` option of the page only restricts the other documents.

The list does not change when the page registers or unregisters a tool: call the method again after running the event
loop. A page that registers its tools asynchronously (after fetching their configuration, for example) can call
`document.modelContext.waitForToolsUntil(promise)`:
[BrowsingContext.ModelContextToolsReady](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContext.ModelContextToolsReady)
is false until the promises settle.

## Executing a tool

[BrowsingContext.ExecuteModelContextToolAsync](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContext.ExecuteModelContextToolAsync)
takes the JSON text of the arguments (an object; `{}` by default), runs the `execute` callback of the tool in a task of
the event loop, and completes with the JSON text of its result. Call it on the thread of the event loop, then run the event
loop until the task completes: the callback usually returns a promise. The page sees a `toolactivated` event when the
callback starts.

The task fails with a [DomException](cref:Meziantou.Framework.WebPlatform.Dom.DomException):

| Name | Reason |
|---|---|
| `NotFoundError` | The tool was unregistered, or its document navigated to another origin. A tool registered again with the same name is a new tool: list the tools again. |
| `DataError` | The arguments are not the JSON text of an object. |
| `OperationError` | The callback threw or rejected, or its result cannot be serialized to JSON. The message of the page is not given. |
| `AbortError` | The document of the tool was unloaded (navigated, or its iframe removed) before the tool completed. |
| `InvalidStateError` | The document of the tool was destroyed, or belongs to another frame tree. |

The cancellation token cancels the execution: the task is canceled, the `AbortSignal` given to the callback is aborted, and
the page sees a `toolcancel` event.

## What the scripts can use

The pages use the API of the specification on `document.modelContext`, in the secure contexts (`https:`, `localhost`):
`registerTool()` (with the `exposedTo` and `signal` options), `getTools()` (with `fromOrigins`), `executeTool()` (with
`signal`), the `toolchange`, `toolactivated` and `toolcancel` events, and the `Tool` objects that `getTools()` returns.
Their behavior follows the specification and the proposals that Chromium implements:

- `getTools()` returns `Tool` objects, which identify a registration of a tool
  ([webmcp#348](https://github.com/webmachinelearning/webmcp/pull/348)): executing a tool that was unregistered fails,
  even when a tool with the same name was registered again.
- `waitForToolsUntil()` and `toolsReady`
  ([webmcp#373](https://github.com/webmachinelearning/webmcp/pull/373)).
- `executeTool()` resolves with the JSON serialization of the result of the tool: a string is quoted, and `undefined` is
  `null` ([webmcp#361](https://github.com/webmachinelearning/webmcp/issues/361)).

The tools are restricted to the documents of the frame tree that are same origin with the document that registered them,
unless the tool lists other origins in `exposedTo` and the caller lists its origin in `fromOrigins`. The iframes need the
`tools` permissions policy feature (`<iframe allow="tools">`) for a cross-origin document. The declarative API of the
[explainer](https://github.com/webmachinelearning/webmcp/blob/main/declarative-api-explainer.md) (the `toolname`
attribute of the forms) is not implemented.

`new WebPlatformFeatures { WebMcpEnabled = false }` in
[BrowsingContextOptions.Features](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.Features) hides
`document.modelContext` and the WebMCP interfaces from the scripts (see
[WebPlatformFeatures.WebMcpEnabled](cref:Meziantou.Framework.WebPlatform.Dom.WebPlatformFeatures.WebMcpEnabled)).
