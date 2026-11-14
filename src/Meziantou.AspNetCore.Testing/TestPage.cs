using System.Diagnostics;
using System.Runtime.CompilerServices;
using Meziantou.AspNetCore.Testing.Internals;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Imaging;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.AspNetCore.Testing;

/// <summary>
/// A page of a browser engine written in .NET (Meziantou.Framework.WebPlatform), driven by a test: the user input, the
/// queries of the elements, the waits, and the snapshots of the DOM, of the accessibility tree and of the pixels. A
/// <see cref="BrowserPage"/> is a page of an application (see <see cref="WebAppBrowser"/>).
/// </summary>
public abstract class TestPage : LocatorScope, IAsyncDisposable, IDisposable
{
    private static readonly ConditionalWeakTable<Document, TestPage> Pages = [];

    private readonly BrowsingContext _browsingContext;
    private readonly ScriptEngine? _scriptEngine;
    private readonly List<string> _consoleMessages = [];
    private readonly List<ScriptError> _scriptErrors = [];
    private readonly Lock _signalLock = new();
    private TaskCompletionSource _workSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="TestPage"/> class.</summary>
    /// <param name="settings">The settings of the browsing context of the page.</param>
    protected TestPage(TestPageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        DefaultTimeout = settings.DefaultTimeout;
        TestIdAttribute = settings.TestIdAttribute;
        _scriptEngine = settings.ScriptEngine;
        _browsingContext = new BrowsingContext(new BrowsingContextOptions
        {
            Viewport = settings.Viewport,
            DocumentLoader = settings.DocumentLoader,
            CookieJar = settings.CookieJar,
            RenderingBackend = new ImagingRenderingBackend(),
            ScriptEngine = settings.ScriptEngine,
            TimeProvider = settings.TimeProvider,
            ConsoleMessage = (_, message) =>
            {
                lock (_consoleMessages)
                {
                    _consoleMessages.Add(message.Text);
                }
            },
            UnhandledScriptError = (_, error) =>
            {
                lock (_scriptErrors)
                {
                    _scriptErrors.Add(error);
                }
            },

            // The elements of the next documents (navigations) find their page too
            DocumentActivated = (_, document) => Pages.AddOrUpdate(document, this),
        });

        _browsingContext.EventLoop.WorkSignal = SignalWork;
        Pages.AddOrUpdate(_browsingContext.Document, this);
    }

    /// <summary>Gets the browsing context of the page (viewport, user input, event loop, history...).</summary>
    public BrowsingContext BrowsingContext => _browsingContext;

    /// <summary>Gets the active document of the page.</summary>
    public Document Document => _browsingContext.Document;

    /// <summary>Gets the URL of the active document.</summary>
    public string Url => Document.Url;

    /// <summary>Gets the user input: the mouse, the keyboard and the touch screen.</summary>
    public PageInput Input => field ??= new PageInput(this);

    /// <summary>Gets the messages written by the scripts of the page to the console.</summary>
    public IReadOnlyList<string> ConsoleMessages
    {
        get
        {
            lock (_consoleMessages)
            {
                return [.. _consoleMessages];
            }
        }
    }

    /// <summary>Gets the errors of the scripts of the page that were not handled.</summary>
    public IReadOnlyList<ScriptError> ScriptErrors
    {
        get
        {
            lock (_scriptErrors)
            {
                return [.. _scriptErrors];
            }
        }
    }

    /// <summary>Gets the maximum time the waits of the page wait for.</summary>
    public TimeSpan DefaultTimeout { get; }

    /// <summary>Gets the name of the attribute that <see cref="LocatorScope.GetByTestId(string)"/> matches.</summary>
    public string TestIdAttribute { get; }

    /// <summary>Gets whether the page is disposed.</summary>
    protected bool IsDisposed => _disposed;

    /// <summary>Gets the first element of the page that matches a CSS selector, now (see <see cref="LocatorScope.Locator(string)"/> for a locator that waits).</summary>
    /// <param name="selector">The CSS selector.</param>
    /// <returns>The element.</returns>
    /// <exception cref="ElementNotFoundException">No element matches the selector.</exception>
    public Element Find(string selector) => FindOrDefault(selector) ?? throw new ElementNotFoundException($"No element matches the selector '{selector}'");

    /// <summary>Gets the first element of the page of a type that matches a CSS selector, now.</summary>
    /// <typeparam name="TElement">The type of the element.</typeparam>
    /// <param name="selector">The CSS selector.</param>
    /// <returns>The element.</returns>
    /// <exception cref="ElementNotFoundException">No element of type <typeparamref name="TElement"/> matches the selector.</exception>
    public TElement Find<TElement>(string selector)
        where TElement : Element
        => FindAll(selector).OfType<TElement>().FirstOrDefault() ?? throw new ElementNotFoundException($"No {typeof(TElement).Name} matches the selector '{selector}'");

    /// <summary>Gets the first element of the page that matches a CSS selector now, or null.</summary>
    /// <param name="selector">The CSS selector.</param>
    /// <returns>The element, or null.</returns>
    public Element? FindOrDefault(string selector) => FindAll(selector) is [var first, ..] ? first : null;

    /// <summary>Gets the elements of the page that match a CSS selector now, in document order.</summary>
    /// <param name="selector">The CSS selector.</param>
    /// <returns>The elements.</returns>
    public IReadOnlyList<Element> FindAll(string selector) => Locator(selector).GetElements();

    /// <summary>Gets the HTML of the body of the page, formatted for snapshots.</summary>
    /// <param name="options">The options, or null for the default options.</param>
    /// <returns>The HTML.</returns>
    public string GetMarkup(MarkupOptions? options = null) => DomSnapshot.GetMarkup(Document.Body is { } body ? [.. body.ChildNodes] : [], options);

    /// <summary>Gets the accessibility tree of the page, as a YAML snapshot (the format of the ARIA snapshots of Playwright).</summary>
    /// <returns>The snapshot.</returns>
    public string GetAccessibilitySnapshot() => Document.Body is { } body ? DomSnapshot.GetAccessibilitySnapshot([body]) : "";

    /// <summary>Takes a screenshot of the viewport, encoded as PNG.</summary>
    /// <param name="options">The options, or null for the default options.</param>
    /// <returns>The PNG image.</returns>
    public byte[] TakeScreenshotAsPng(ScreenshotOptions? options = null)
    {
        using (EnterEventLoop())
        {
            return _browsingContext.TakeScreenshotAsPng(options);
        }
    }

    /// <summary>
    /// Runs the event loop of the page until it is idle: no task, no pending network request (see <see cref="IsIdle"/>).
    /// </summary>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the page is idle.</returns>
    /// <exception cref="TimeoutException">The page is not idle after <paramref name="timeout"/>.</exception>
    public Task WaitForIdleAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => WaitCoreAsync(condition: null, timeout, cancellationToken);

    /// <summary>Runs the event loop of the page until a condition is true.</summary>
    /// <param name="condition">The condition, evaluated after each task of the event loop.</param>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the condition is true.</returns>
    /// <exception cref="TimeoutException">The condition is false after <paramref name="timeout"/>.</exception>
    public Task WaitForAsync(Func<bool> condition, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return WaitCoreAsync(condition, timeout, cancellationToken);
    }

    /// <summary>Runs the event loop of the page until an assertion passes.</summary>
    /// <param name="assertion">The assertion, which throws when it fails.</param>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    /// <exception cref="TimeoutException">The assertion fails after <paramref name="timeout"/>; the inner exception is the last failure.</exception>
    public async Task WaitForAssertionAsync(Action assertion, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assertion);
        Exception? lastFailure = null;
        try
        {
            await WaitCoreAsync(() =>
            {
                try
                {
                    assertion();
                    return true;
                }
                catch (Exception ex)
                {
                    lastFailure = ex;
                    return false;
                }
            }, timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex) when (lastFailure is not null)
        {
            throw new TimeoutException(ex.Message, lastFailure);
        }
    }

    /// <summary>Gets the page of a node of one of its documents.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The page.</returns>
    /// <exception cref="InvalidOperationException">The node does not belong to a document of a <see cref="TestPage"/>.</exception>
    public static TestPage GetPage(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var document = node as Document ?? node.OwnerDocument;
        if (document is not null && Pages.TryGetValue(document, out var page))
            return page;

        throw new InvalidOperationException("The node does not belong to a document of a TestPage");
    }

    /// <summary>
    /// Runs the code that follows on the event loop of the page until the result is disposed: a derived page runs the
    /// continuations of its own asynchronous code on the event loop (for instance the components of Blazor). Returns null by default.
    /// </summary>
    /// <returns>An object that leaves the event loop when disposed, or null.</returns>
    protected internal virtual IDisposable? EnterEventLoop() => null;

    /// <summary>
    /// Returns whether the page is idle: the event loop has no task and no pending network request. A derived page adds its
    /// own pending work.
    /// </summary>
    /// <returns>True when the page is idle.</returns>
    protected virtual bool IsIdle()
    {
        var eventLoop = _browsingContext.EventLoop;
        return !eventLoop.HasPendingWork && !eventLoop.HasPendingOperations;
    }

    /// <summary>Returns the tasks whose completion can make the page idle (a wait wakes up when one completes).</summary>
    /// <returns>The pending tasks.</returns>
    protected virtual IReadOnlyCollection<Task> GetPendingTasks() => [];

    /// <summary>Throws the errors that the page collected while the event loop ran (for instance the unhandled exceptions of the components).</summary>
    protected virtual void ThrowIfFailed()
    {
    }

    /// <summary>Gets the nodes that the queries of the page search: the document element by default.</summary>
    /// <returns>The root nodes.</returns>
    protected virtual IReadOnlyList<Node> GetRoots() => Document.DocumentElement is { } root ? [root] : [];

    private protected override string TestIdAttributeCore => TestIdAttribute;

    private protected override Locator CreateLocator(LocatorStep step) => Testing.Locator.Create(this, GetRoots, step);

    /// <summary>Runs the tasks of the event loop of the page until there is none.</summary>
    protected internal void RunEventLoop()
    {
        using (EnterEventLoop())
        {
            _browsingContext.EventLoop.RunUntilIdle();
        }
    }

    private async Task WaitCoreAsync(Func<bool>? condition, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var actualTimeout = timeout ?? DefaultTimeout;
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task signal;
            lock (_signalLock)
            {
                signal = _workSignal.Task;
            }

            RunEventLoop();
            ThrowIfFailed();

            bool done;
            using (EnterEventLoop())
            {
                done = condition is null ? IsIdle() : condition();
            }

            if (done)
                return;

            var remaining = actualTimeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException(condition is null ? $"The page is not idle after {actualTimeout}" : $"The condition is not true after {actualTimeout}");

            // Wake up when a task is queued (a continuation, a response...), when a pending task completes, when the next
            // timer of the page is due, or periodically to evaluate the condition again
            var delay = remaining;
            if (_browsingContext.EventLoop.PromoteDueTimers() is { } nextTimer && nextTimer < delay)
            {
                delay = nextTimer;
            }

            if (condition is not null && delay > TimeSpan.FromMilliseconds(50))
            {
                delay = TimeSpan.FromMilliseconds(50);
            }

            var waits = new List<Task> { signal, Task.Delay(delay, cancellationToken) };
            if (GetPendingTasks() is { Count: > 0 } pendingTasks)
            {
                waits.Add(Task.WhenAny(pendingTasks));
            }

            await Task.WhenAny(waits).ConfigureAwait(false);
        }
    }

    private void SignalWork()
    {
        TaskCompletionSource signal;
        lock (_signalLock)
        {
            signal = _workSignal;
            _workSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        signal.TrySetResult();
    }

    /// <summary>Releases the resources of the page.</summary>
    /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        _disposed = true;
        if (disposing)
        {
            _browsingContext.Dispose();
            (_scriptEngine as IDisposable)?.Dispose();
        }
    }

    /// <summary>Releases the resources of the page asynchronously. Calls <see cref="Dispose(bool)"/> by default.</summary>
    /// <returns>A task completed once the resources are released.</returns>
    protected virtual ValueTask DisposeAsyncCore()
    {
        Dispose(disposing: true);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
