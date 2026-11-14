using Meziantou.AspNetCore.Testing.Components.Internals;
using Meziantou.AspNetCore.Testing.Components.JSInterop;
using Meziantou.AspNetCore.Testing.Components.Navigation;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.JavaScript;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Meziantou.AspNetCore.Testing.Components;

/// <summary>
/// A browser that renders Blazor components: the components are interactive (as in Blazor WebAssembly), their output is
/// the DOM of a page of Meziantou.Framework.WebPlatform, with its styles, its layout and its user input.
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.AspNetCore.Testing.Tests/ComponentTestHostTests.cs" ref:region="ComponentTestHost_Counter" ref:dedent="true">
/// await using var host = new ComponentTestHost();
/// var counter = await host.RenderAsync&lt;Counter&gt;(parameters =&gt; parameters.Add(c =&gt; c.IncrementAmount, 2));
///
/// // A real click: the pointer moves to the center of the button, the hit testing finds the button
/// await counter.Find("button").ClickAsync();
/// await counter.GetByRole("button", "Click me").ClickAsync();
///
/// Assert.Equal("Current count: 4", counter.GetByRole("status").TextContent);
/// Assert.Equal("""
///     &lt;p role="status"&gt;Current count: 4&lt;/p&gt;
///     &lt;button class="btn"&gt;Click me&lt;/button&gt;
///     """, counter.Markup);
/// </code>
/// </example>
public sealed class ComponentTestHost : TestPage
{
    private readonly ComponentTestHostOptions _options;
    private readonly EventLoopDispatcher _dispatcher;
    private readonly PageJSRuntime? _pageJSRuntime;
    private readonly TestJSRuntime _jsRuntime;
    private ServiceProvider? _serviceProvider;
    private WebPlatformRenderer? _renderer;
    private bool _bodyHostsRootComponent;

    /// <summary>Initializes a new instance of the <see cref="ComponentTestHost"/> class.</summary>
    /// <param name="options">The options, or null for the default options.</param>
    public ComponentTestHost(ComponentTestHostOptions? options = null)
        : this(options ?? new ComponentTestHostOptions(), CreateScriptEngine(options ?? new ComponentTestHostOptions()))
    {
    }

    private ComponentTestHost(ComponentTestHostOptions options, ComponentScriptEngine? scriptEngine)
        : base(new TestPageSettings
        {
            Viewport = options.Viewport,
            DocumentLoader = options.DocumentLoader ?? TestDocumentLoaders.FromDirectories(new Uri(options.BaseUrl, UriKind.Absolute), options.StaticFiles),
            ScriptEngine = scriptEngine,
            TimeProvider = options.TimeProvider,
            DefaultTimeout = options.DefaultTimeout,
            TestIdAttribute = options.TestIdAttribute,
        })
    {
        _options = options;
        _dispatcher = new EventLoopDispatcher(BrowsingContext.EventLoop);
        if (scriptEngine is not null)
        {
            _pageJSRuntime = new PageJSRuntime(scriptEngine, () => BrowsingContext, _dispatcher);
        }

        JSInterop = new ComponentJSInterop();
        _jsRuntime = new TestJSRuntime(JSInterop, () => Document, () => _pageJSRuntime);
        NavigationManager = new TestNavigationManager();

        // Load the host page and its resources (style sheets, scripts...)
        var baseUri = new Uri(_options.BaseUrl, UriKind.Absolute);
        using (_dispatcher.Enter())
        {
            var html = _options.HostPage;
            if (_options.StyleSheets.Count > 0)
            {
                var styles = string.Concat(_options.StyleSheets.Select(css => "<style>" + css + "</style>"));
                var headEnd = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
                html = headEnd >= 0 ? html.Insert(headEnd, styles) : styles + html;
            }

            BrowsingContext.Navigate(baseUri.AbsoluteUri, _ => html);
            BrowsingContext.EventLoop.RunUntilIdle();
        }

        NavigationManager.Initialize(baseUri.AbsoluteUri, baseUri.AbsoluteUri, () => BrowsingContext);
        Services = new ServiceCollection();
        Services.TryAddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.TryAdd(ServiceDescriptor.Singleton(typeof(ILogger<>), typeof(NullLogger<>)));
        Services.AddSingleton<IJSRuntime>(_jsRuntime);
        Services.AddSingleton<NavigationManager>(NavigationManager);
        Services.AddSingleton(NavigationManager);
        var interception = new NavigationInterception(NavigationManager, () => BrowsingContext, () => _dispatcher);
        Services.AddSingleton<INavigationInterception>(interception);
        Services.AddSingleton<IScrollToLocationHash>(interception);
    }

    /// <summary>Gets the services of the components. Add services before the first component is rendered.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Gets the services of the components. The first access builds them from <see cref="Services"/>.</summary>
    public IServiceProvider ServiceProvider => _serviceProvider ??= Services.BuildServiceProvider();

    /// <summary>Gets the JavaScript interop of the components.</summary>
    public ComponentJSInterop JSInterop { get; }

    /// <summary>Gets the navigation manager of the components.</summary>
    public TestNavigationManager NavigationManager { get; }

    internal EventLoopDispatcher Dispatcher => _dispatcher;

    internal WebPlatformRenderer Renderer
    {
        get
        {
            if (_renderer is null)
            {
                var loggerFactory = ServiceProvider.GetRequiredService<ILoggerFactory>();
                _renderer = new WebPlatformRenderer(ServiceProvider, loggerFactory, _dispatcher, Document, _options.CustomEventArgs.AsReadOnly());
                _renderer.SetElementReferenceContext(new WebElementReferenceContext(_jsRuntime));
            }

            return _renderer;
        }
    }

    /// <summary>
    /// Renders a component, and waits until the component and its descendants are idle (their asynchronous lifecycle
    /// methods and the tasks of the page are completed).
    /// </summary>
    /// <typeparam name="TComponent">The type of the component.</typeparam>
    /// <param name="parameters">Sets the parameters of the component, or null.</param>
    /// <param name="options">Where the component is rendered, or null to render it in the <c>body</c> of the page.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>The rendered component.</returns>
    public async Task<RenderedComponent<TComponent>> RenderAsync<TComponent>(Action<ComponentParameterCollectionBuilder<TComponent>>? parameters = null, RenderOptions? options = null, CancellationToken cancellationToken = default)
        where TComponent : IComponent
    {
        var component = Render(parameters, options);
        await WaitForIdleAsync(timeout: null, cancellationToken).ConfigureAwait(false);
        return component;
    }

    /// <summary>
    /// Renders a component without waiting for its asynchronous lifecycle methods: the result shows the first render
    /// (for instance a loading indicator). Use <see cref="TestPage.WaitForIdleAsync"/> or <see cref="TestPage.WaitForAsync"/> afterwards.
    /// </summary>
    /// <typeparam name="TComponent">The type of the component.</typeparam>
    /// <param name="parameters">Sets the parameters of the component, or null.</param>
    /// <param name="options">Where the component is rendered, or null to render it in the <c>body</c> of the page.</param>
    /// <returns>The rendered component.</returns>
    public RenderedComponent<TComponent> Render<TComponent>(Action<ComponentParameterCollectionBuilder<TComponent>>? parameters = null, RenderOptions? options = null)
        where TComponent : IComponent
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var builder = new ComponentParameterCollectionBuilder<TComponent>();
        parameters?.Invoke(builder);

        RenderedComponent<TComponent> result;
        using (_dispatcher.Enter())
        {
            var renderer = Renderer;
            var container = GetContainer(options);
            var component = (TComponent)renderer.CreateComponent(typeof(TComponent));
            var componentId = renderer.AssignRootComponentId(component);
            renderer.Applier.AttachRootComponent(componentId, container);
            _ = renderer.RenderRootAsync(componentId, builder.Build());
            result = new RenderedComponent<TComponent>(this, componentId, component);
            RunEventLoop();
        }

        ThrowIfFailed();
        return result;
    }

    /// <summary>Renders a fragment (for instance <c>@&lt;MyComponent Value="1" /&gt;</c> in a Razor test), and waits until it is idle.</summary>
    /// <param name="fragment">The fragment.</param>
    /// <param name="options">Where the fragment is rendered, or null to render it in the <c>body</c> of the page.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>The rendered fragment.</returns>
    public Task<RenderedComponent<FragmentContainer>> RenderAsync(RenderFragment fragment, RenderOptions? options = null, CancellationToken cancellationToken = default)
        => RenderAsync<FragmentContainer>(parameters => parameters.Add(container => container.ChildContent, fragment), options, cancellationToken);

    /// <summary>Gets the element of an <see cref="ElementReference"/> captured by a component.</summary>
    /// <param name="reference">The reference.</param>
    /// <returns>The element, or null when it is not in the document.</returns>
    public Element? GetElement(ElementReference reference) => _jsRuntime.GetElement(reference);

    /// <summary>Navigates to a URL of the application, as a click on a link does (the router renders the page of the URL).</summary>
    /// <param name="uri">The URL, relative to the base URL.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the components are idle.</returns>
    public Task NavigateAsync(string uri, CancellationToken cancellationToken = default)
        => InvokeAsync(() => NavigationManager.NavigateTo(uri), cancellationToken);

    /// <summary>Runs code on the dispatcher of the components (as <c>ComponentBase.InvokeAsync</c>), then waits until the components are idle.</summary>
    /// <param name="action">The code.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the components are idle.</returns>
    public Task InvokeAsync(Action action, CancellationToken cancellationToken = default) => InvokeAsync(() =>
    {
        action();
        return Task.CompletedTask;
    }, cancellationToken);

    /// <summary>Runs code on the dispatcher of the components (as <c>ComponentBase.InvokeAsync</c>), then waits until the components are idle.</summary>
    /// <param name="action">The code.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the components are idle.</returns>
    public async Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        Task task;
        using (_dispatcher.Enter())
        {
            task = action();
        }

        await WaitForAsync(() => task.IsCompleted, timeout: null, cancellationToken).ConfigureAwait(false);
        await task.ConfigureAwait(false);
        await WaitForIdleAsync(timeout: null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected internal override IDisposable? EnterEventLoop() => _dispatcher?.Enter();

    /// <inheritdoc />
    protected override bool IsIdle() => base.IsIdle() && !(_renderer?.HasPendingTasks ?? false);

    /// <inheritdoc />
    protected override IReadOnlyCollection<Task> GetPendingTasks() => _renderer?.GetPendingTasks() ?? [];

    /// <inheritdoc />
    protected override void ThrowIfFailed()
    {
        if (_renderer?.TakeUnhandledExceptions() is { Length: > 0 } exceptions)
        {
            if (exceptions.Length == 1)
                throw new ComponentException(exceptions[0].Message, exceptions[0]);

            throw new ComponentException("The components threw unhandled exceptions", new AggregateException(exceptions));
        }
    }

    private static ComponentScriptEngine? CreateScriptEngine(ComponentTestHostOptions options)
        => options.EnableJavaScript ? new ComponentScriptEngine(new JavaScriptScriptEngineOptions { Timeout = options.DefaultTimeout }) : null;

    private Element GetContainer(RenderOptions? options)
    {
        if (options?.Container is { } container)
            return container;

        if (options?.ContainerSelector is { } selector)
            return Document.QuerySelector(selector) ?? throw new InvalidOperationException($"No element matches the selector '{selector}'");

        var body = Document.Body ?? throw new InvalidOperationException("The host page has no body");
        if (!_bodyHostsRootComponent)
        {
            _bodyHostsRootComponent = true;
            return body;
        }

        var div = Document.CreateElement("div");
        body.AppendChild(div);
        return div;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!IsDisposed && disposing)
        {
            if (_renderer is not null)
            {
                using (_dispatcher.Enter())
                {
                    _renderer.Dispose();
                }
            }

            _serviceProvider?.Dispose();
            _pageJSRuntime?.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeAsyncCore()
    {
        if (!IsDisposed)
        {
            // The components and the services release their JavaScript objects when they are disposed: the calls run on
            // the event loop of the page, so the event loop runs until the disposal completes
            if (_renderer is not null)
            {
                Task disposeTask;
                using (_dispatcher.Enter())
                {
                    disposeTask = _renderer.DisposeAsync().AsTask();
                }

                await RunEventLoopUntilCompletedAsync(disposeTask).ConfigureAwait(false);
                _renderer = null;
            }

            if (_serviceProvider is not null)
            {
                Task disposeTask;
                using (_dispatcher.Enter())
                {
                    disposeTask = _serviceProvider.DisposeAsync().AsTask();
                }

                await RunEventLoopUntilCompletedAsync(disposeTask).ConfigureAwait(false);
                _serviceProvider = null;
            }
        }

        await base.DisposeAsyncCore().ConfigureAwait(false);
    }

    private async Task RunEventLoopUntilCompletedAsync(Task task)
    {
        try
        {
            await WaitForAsync(() => task.IsCompleted).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // The page does not answer anymore (for instance a script that never resolves its promise): the disposal
            // must not block the test
            return;
        }

        await task.ConfigureAwait(false);
    }
}
