using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.JavaScript;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.JavaScript;

/// <summary>WebMCP (https://webmachinelearning.github.io/webmcp/): <c>document.modelContext</c> and the tools of the pages read by the application.</summary>
public sealed class ModelContextTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly ScriptEngine _engine = TestScriptEngines.Create();

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private BrowsingContext Load(string html, string url = "https://example.com/", WebPlatformFeatures? features = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            UnhandledScriptError = (_, error) => _errors.Add(error),
            Features = features ?? new WebPlatformFeatures(),
        });

        context.Navigate(url, _ => html);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return context;
    }

    private string Run(BrowsingContext context, string? script = null)
    {
        if (script is not null)
        {
            TestScriptEngines.Evaluate(_engine, context.Window, script);
        }

        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return context.Document.Title;
    }

    private const string AddTool = """
        <script>
        window.log = [];
        document.modelContext.ontoolactivated = e => log.push('activated:' + e.toolName);
        document.modelContext.ontoolcancel = e => log.push('cancel:' + e.toolName);
        document.modelContext.registerTool({
          name: 'add',
          title: 'Add',
          description: 'Adds two numbers',
          inputSchema: { type: 'object', properties: { a: { type: 'number' }, b: { type: 'number' } } },
          annotations: { readOnlyHint: true },
          execute: ({ a, b }) => ({ sum: a + b }),
        });
        document.modelContext.registerTool({
          name: 'wait',
          description: 'Never completes',
          execute: (input, { signal }) => new Promise((resolve, reject) => signal.onabort = () => { log.push('aborted:' + signal.reason.name); reject(signal.reason); }),
        });
        </script>
        """;

    [Fact]
    public async Task Host_ListsAndExecutesTheToolsOfThePage()
    {
        #region WebPlatform_WebMcp
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
        #endregion
    }

    [Fact]
    public async Task Host_ExecuteTool_FiresTheEventsAndReportsTheErrors()
    {
        var context = Load(AddTool);
        var tools = context.GetModelContextTools();
        Assert.Equal(["add", "wait"], tools.Select(tool => tool.Name).ToArray());
        Assert.Equal("Add", tools[0].Title);
        Assert.Equal("", tools[1].Title);
        Assert.Null(tools[1].InputSchemaJson);
        Assert.Null(tools[1].ToolAnnotations);
        Assert.Same(context.Document, tools[0].Document);

        var execution = context.ExecuteModelContextToolAsync(tools[0], """{"a":40,"b":2}""");
        Run(context);
        Assert.Equal("""{"sum":42}""", await execution);
        Assert.Equal("activated:add", TestScriptEngines.Evaluate(_engine, context.Window, "log.join()"));

        // The arguments must be a JSON object
        var invalid = context.ExecuteModelContextToolAsync(tools[0], "[1");
        Run(context);
        Assert.Equal("DataError", (await Assert.ThrowsAsync<DomException>(() => invalid)).Name);

        var notAnObject = context.ExecuteModelContextToolAsync(tools[0], "42");
        Run(context);
        Assert.Equal("DataError", (await Assert.ThrowsAsync<DomException>(() => notAnObject)).Name);
    }

    [Fact]
    public async Task Host_ExecuteTool_Cancellation_AbortsTheSignalOfTheTool()
    {
        var context = Load(AddTool);
        var wait = context.GetModelContextTools().Single(tool => tool.Name == "wait");

        using var cancellation = new CancellationTokenSource();
        var execution = context.ExecuteModelContextToolAsync(wait, cancellationToken: cancellation.Token);
        Run(context);
        Assert.False(execution.IsCompleted);

        await cancellation.CancelAsync();
        Run(context);
        await Assert.ThrowsAsync<TaskCanceledException>(() => execution);
        Assert.Equal("activated:wait,aborted:AbortError,cancel:wait", TestScriptEngines.Evaluate(_engine, context.Window, "log.join()"));
    }

    [Fact]
    public async Task Host_ExecuteTool_UnregisteredTool_IsNotFound()
    {
        var context = Load("""
            <script>
            const controller = new AbortController();
            document.modelContext.registerTool({ name: 'tool', description: 'Tool', execute: () => 'result' }, { signal: controller.signal });
            window.unregister = () => controller.abort();
            </script>
            """);
        var tool = Assert.Single(context.GetModelContextTools());
        Run(context, "unregister()");

        Assert.Empty(context.GetModelContextTools());
        Assert.Equal("NotFoundError", (await Assert.ThrowsAsync<DomException>(() => context.ExecuteModelContextToolAsync(tool))).Name);
    }

    [Fact]
    public async Task Host_ExecuteTool_DocumentUnloaded_IsAborted()
    {
        var context = Load(AddTool);
        var wait = context.GetModelContextTools().Single(tool => tool.Name == "wait");
        var execution = context.ExecuteModelContextToolAsync(wait);
        Run(context);

        context.Navigate("https://example.com/other", _ => "<p>other</p>");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("AbortError", (await Assert.ThrowsAsync<DomException>(() => execution)).Name);
        Assert.Empty(context.GetModelContextTools());
    }

    [Fact]
    public void Host_ToolsOfIframes_AreListed()
    {
        var context = Load("""
            <iframe srcdoc="<script>document.modelContext.registerTool({ name: 'child', description: 'Child tool', execute: () => 1 })</script>"></iframe>
            <script>document.modelContext.registerTool({ name: 'parent', description: 'Parent tool', execute: () => 1 })</script>
            """);

        var tools = context.GetModelContextTools();
        Assert.Equal(["parent", "child"], tools.Select(tool => tool.Name).ToArray());
        Assert.NotSame(tools[0].Document, tools[1].Document);
    }

    [Fact]
    public void Script_RegistersDiscoversAndExecutesTools()
    {
        var context = Load(AddTool + """
            <script>
            document.modelContext.getTools().then(async tools => {
              const add = tools.find(tool => tool.name === 'add');
              const result = await document.modelContext.executeTool(add, { a: 1, b: 2 });
              document.title = [tools.map(tool => tool.name).join(), add instanceof Tool, add.window === window, add.origin, JSON.stringify(add.annotations), add.inputSchema === add.inputSchema, result].join('|');
            });
            </script>
            """);

        Assert.Equal("""add,wait|true|true|https://example.com|{"consequentialHint":false,"debugging":false,"readOnlyHint":true,"untrustedContentHint":false}|true|{"sum":3}""", Run(context));
    }

    [Fact]
    public void Script_ExecuteTool_SerializesTheResultToJson()
    {
        // https://github.com/webmachinelearning/webmcp/issues/361: a string is serialized to JSON, undefined is null
        var context = Load("""
            <script>
            (async () => {
              await document.modelContext.registerTool({ name: 'text', description: 'Text', execute: () => 'ok' });
              await document.modelContext.registerTool({ name: 'nothing', description: 'Nothing', execute: () => {} });
              await document.modelContext.registerTool({ name: 'circular', description: 'Circular', execute: () => { const a = {}; a.a = a; return a; } });
              await document.modelContext.registerTool({ name: 'throws', description: 'Throws', execute: () => { throw new Error('error'); } });
              const tools = await document.modelContext.getTools();
              const results = [];
              for (const tool of tools) {
                results.push(await document.modelContext.executeTool(tool).catch(e => e.name));
              }
              document.title = results.join('|');
            })();
            </script>
            """);

        Assert.Equal("""OperationError|null|"ok"|OperationError""", Run(context));
    }

    [Fact]
    public async Task Host_ExecuteTool_PageReplacingJsonFunctions_DoesNotChangeTheArgumentsAndTheResult()
    {
        var context = Load(AddTool + """
            <script>
            JSON.parse = () => ({ a: 100, b: 100 });
            JSON.stringify = () => '"replaced"';
            </script>
            """);
        var add = context.GetModelContextTools().Single(tool => tool.Name == "add");

        var execution = context.ExecuteModelContextToolAsync(add, """{"a":1,"b":2}""");
        Run(context);
        Assert.Equal("""{"sum":3}""", await execution);
    }

    [Fact]
    public void Script_ExecuteTool_CallerDetachedBySerialization_IsRejectedWithInvalidStateError()
    {
        var context = Load("""
            <iframe></iframe>
            <script>
            document.modelContext.registerTool({ name: 'tool', description: 'Tool', execute: () => 'ok' });
            const frame = document.querySelector('iframe');
            const frameModelContext = frame.contentDocument.modelContext;
            document.modelContext.getTools().then(([tool]) => {
              frameModelContext.executeTool(tool, { toJSON() { frame.remove(); return {}; } }).then(() => document.title = 'resolved', e => document.title = e.name);
            });
            </script>
            """);

        Assert.Equal("InvalidStateError", Run(context));
    }

    [Fact]
    public void Script_RegisterTool_ValidatesTheTool()
    {
        var context = Load("""
            <script>
            const register = (tool, options) => document.modelContext.registerTool({ description: 'Tool', execute: () => {}, ...tool }, options).then(() => 'ok', e => e.name);
            Promise.all([
              register({ name: 'valid-name.with_extras' }),
              register({ name: 'valid-name.with_extras' }),
              register({ name: '' }),
              register({ name: 'a'.repeat(129) }),
              register({ name: 'name with space' }),
              register({ name: 'tool', description: '' }),
              register({ name: 'schema', inputSchema: { toJSON: () => undefined } }),
              register({ name: 'exposed', }, { exposedTo: ['http://example.org'] }),
              register({ name: 'exposed' }, { exposedTo: ['https://example.org', 'http://localhost:3000'] }),
              register({ name: 'aborted' }, { signal: AbortSignal.abort() }),
            ]).then(results => document.title = results.join());
            </script>
            """);

        Assert.Equal("ok,InvalidStateError,InvalidStateError,InvalidStateError,InvalidStateError,InvalidStateError,TypeError,SecurityError,ok,AbortError", Run(context));
    }

    [Fact]
    public void Script_ToolChange_IsFiredBeforeTheRegistrationIsResolved()
    {
        var context = Load("""
            <script>
            const events = [];
            const log = event => document.title = (events.push(event), events.join());
            document.modelContext.addEventListener('toolchange', () => log('toolchange'));
            const controller = new AbortController();
            document.modelContext.registerTool({ name: 'tool', description: 'Tool', execute: () => {} }, { signal: controller.signal })
              .then(() => { log('resolved'); controller.abort(); });
            </script>
            """);

        Assert.Equal("toolchange,resolved,toolchange", Run(context));
    }

    [Fact]
    public void Script_WaitForToolsUntil_SetsTheReadiness()
    {
        var context = Load("""
            <script>
            let resolve;
            document.modelContext.waitForToolsUntil(new Promise(r => resolve = r));
            window.finish = () => resolve();
            </script>
            """);

        Assert.False(context.ModelContextToolsReady);
        Assert.Equal("false", TestScriptEngines.Evaluate(_engine, context.Window, "String(document.modelContext.toolsReady)"));

        Run(context, "finish()");
        Assert.True(context.ModelContextToolsReady);
    }

    [Fact]
    public void Script_NotSecureContextOrDisabled_HasNoModelContext()
    {
        Assert.Equal("false|undefined", Run(Load("<script>document.title = ('modelContext' in document) + '|' + typeof ModelContext</script>", url: "http://example.com/")));
        Assert.Equal("false|undefined", Run(Load("<script>document.title = ('modelContext' in document) + '|' + typeof Tool</script>", features: new WebPlatformFeatures { WebMcpEnabled = false })));
    }
}
