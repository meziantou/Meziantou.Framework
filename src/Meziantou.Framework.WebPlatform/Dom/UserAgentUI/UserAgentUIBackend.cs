namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The user interface of the user agent (<see cref="BrowsingContextOptions.UserAgentUI"/>): the prompts that the APIs of the
/// web platform show to the user, such as the credential choosers of the Credential Management API, the account chooser of
/// FedCM, the payment sheet of the Payment Request API, the transaction dialog of Secure Payment Confirmation and the wallet
/// of the Digital Credentials API. The library implements the algorithms of these APIs and calls the backend when the user
/// agent would show something: the backend shows the prompt (or answers it without showing anything, e.g. in tests) and
/// returns the answer of the user asynchronously.
/// </summary>
/// <remarks>
/// <para>
/// Each kind of prompt is a virtual method, which receives a description of the prompt (a <see cref="UserAgentPrompt"/>: the
/// window that shows it, the origin of its document, and what the user is asked) and a token that is canceled when the page
/// no longer needs the answer (the request is aborted, its document is unloaded...): the backend should then close the prompt.
/// The default implementation of every method answers as if the user dismissed the prompt, so a backend overrides only the
/// prompts it supports. Without a backend (the default), every prompt is dismissed.
/// </para>
/// <para>
/// The methods are called on the thread of the event loop of the window that shows the prompt; the task they return can
/// complete on any thread (the library settles the promises of the page on its event loop). A backend can be shared by
/// several browsing contexts. An exception thrown by a method is handled as a dismissal of the prompt.
/// </para>
/// </remarks>
public abstract partial class UserAgentUIBackend
{
    /// <summary>Initializes a new instance of the <see cref="UserAgentUIBackend"/> class.</summary>
    protected UserAgentUIBackend()
    {
    }
}

/// <summary>The description of a prompt of the user agent, given to a method of <see cref="UserAgentUIBackend"/>.</summary>
public abstract class UserAgentPrompt
{
    private protected UserAgentPrompt(Window window)
    {
        Context = CallbackContext.For(window);
        Origin = window.Document.Origin.Serialize();
        TopLevelOrigin = window.Document.BrowsingContext?.Top.Document.Origin.Serialize() ?? Origin;
    }

    /// <summary>Gets the context of the prompt: the window that shows it, its browsing context and the options.</summary>
    public CallbackContext Context { get; }

    /// <summary>Gets the serialization of the origin of the document that shows the prompt (e.g. <c>https://example.com</c>), as the prompt displays it.</summary>
    public string Origin { get; }

    /// <summary>Gets the serialization of the origin of the top-level document of the browsing context that shows the prompt.</summary>
    public string TopLevelOrigin { get; }
}

/// <summary>Shows the prompts of <see cref="UserAgentUIBackend"/> and settles their answers on the event loop of the window.</summary>
internal static class UserAgentUI
{
    /// <summary>
    /// Shows a prompt with the backend of the window, then calls <paramref name="complete"/> from a task of the event loop of
    /// the window with the answer of the user: <paramref name="dismissed"/> when there is no backend, the backend throws, or the
    /// prompt is canceled. When the document of the window is not fully active, <paramref name="complete"/> is called at once
    /// with <paramref name="dismissed"/> and the backend is not called. The prompt belongs to the document of the window: when the document is no longer fully active (it is
    /// destroyed, or it enters the back/forward cache), the token given to the backend is canceled and
    /// <paramref name="complete"/> is called at once with <paramref name="dismissed"/>. While the prompt is shown, the event
    /// loop is not idle (until the backend answers, the document is no longer fully active, or
    /// <paramref name="cancellationToken"/> is canceled: the caller settles its promise itself, and the late answer is ignored).
    /// </summary>
    internal static void Show<T>(Window window, Func<UserAgentUIBackend, CancellationToken, ValueTask<T>> prompt, T dismissed, Action<T> complete, CancellationToken cancellationToken = default)
    {
        // A document that is no longer fully active (e.g. a removed iframe whose function was saved) cannot show a prompt: its
        // tasks are no longer run and its prompts were already closed, so the prompt is dismissed at once, without operation
        if (!window.Document.IsFullyActive)
        {
            complete(dismissed);
            return;
        }

        var session = new PromptSession<T>(window, dismissed, complete, cancellationToken);
        if (window.Options.UserAgentUI is not { } backend)
        {
            session.ReleaseSource();
            window.QueueGlobalTask(() => session.Settle(dismissed));
            return;
        }

        ValueTask<T> result;
        try
        {
            result = prompt(backend, session.Token);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            session.ReleaseSource();
            window.QueueGlobalTask(() => session.Settle(dismissed));
            return;
        }

        if (result.IsCompleted)
        {
            var answer = GetAnswer(result.AsTask(), dismissed);
            session.ReleaseSource();
            window.QueueGlobalTask(() => session.Settle(answer));
            return;
        }

        session.Track(result.AsTask());
    }

    private static T GetAnswer<T>(Task<T> task, T dismissed) => task.IsCompletedSuccessfully ? task.Result : dismissed;

    /// <summary>A prompt shown by a document: it is closed when the caller cancels it, or when the document is no longer fully active.</summary>
    private sealed class PromptSession<T>
    {
        private readonly Window _window;
        private readonly Document _document;
        private readonly T _dismissed;
        private readonly Action<T> _complete;
        private readonly CancellationToken _callerToken;
        private readonly CancellationTokenSource _source;
        private readonly CancellationTokenRegistration _callerRegistration;
        private readonly DomEventLoop _eventLoop;
        private int _settled;
        private int _released;

        // The source is disposed once the backend no longer uses its token and the prompt is released (2 references)
        private int _sourceReferences = 2;

        public PromptSession(Window window, T dismissed, Action<T> complete, CancellationToken cancellationToken)
        {
            _window = window;
            _document = window.Document;
            _dismissed = dismissed;
            _complete = complete;
            _callerToken = cancellationToken;
            _source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // The event loop is not idle until the prompt is released. The operation starts before the backend is called: the
            // backend can release the prompt before it returns (e.g. it closes the tab, which closes the prompts of the document)
            _eventLoop = window.EventLoop;
            _eventLoop.BeginOperation();
            _document.AddPromptCloser(OnDocumentClosed);

            // The caller settles its promise itself when it cancels the prompt (e.g. the request is aborted)
            _callerRegistration = cancellationToken.CanBeCanceled ? cancellationToken.Register(Release) : default;
        }

        /// <summary>Gets the token given to the backend: canceled by the caller, or when the document is no longer fully active.</summary>
        public CancellationToken Token => _source.Token;

        /// <summary>Waits for the answer of the backend (the prompt may already be released).</summary>
        public void Track(Task<T> answer)
        {
            _ = answer.ContinueWith(
                completed =>
                {
                    if (Volatile.Read(ref _released) == 0)
                    {
                        var value = GetAnswer(completed, _dismissed);
                        _window.QueueGlobalTask(() => Settle(value));
                    }

                    // The backend no longer uses the token
                    ReleaseSource();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        /// <summary>Calls the completion of the caller with the answer, once, unless the caller canceled the prompt.</summary>
        public void Settle(T answer)
        {
            if (Interlocked.Exchange(ref _settled, 1) != 0)
                return;

            Release();
            if (!_callerToken.IsCancellationRequested)
            {
                _complete(answer);
            }
        }

        /// <summary>The document is destroyed or enters the back/forward cache: the prompt is closed and dismissed.</summary>
        private void OnDocumentClosed()
        {
            if (Volatile.Read(ref _settled) != 0 || Volatile.Read(ref _released) != 0)
                return;

            _source.Cancel();
            Settle(_dismissed);
        }

        /// <summary>The prompt no longer belongs to the document, and no longer keeps the event loop busy.</summary>
        private void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
                return;

            _callerRegistration.Dispose();
            _document.RemovePromptCloser(OnDocumentClosed);
            _eventLoop.EndOperation();
            ReleaseSource();
        }

        /// <summary>Releases a reference to the source of the token: the backend is done with it, or the prompt is released.</summary>
        public void ReleaseSource()
        {
            if (Interlocked.Decrement(ref _sourceReferences) == 0)
            {
                _source.Dispose();
            }
        }
    }
}

// The prompts of the user agent shown by a document
public partial class Document
{
    private readonly Lock _promptClosersLock = new();
    private List<Action>? _promptClosers;

    /// <summary>
    /// Registers the steps that close a prompt of the user agent (or a request that shows prompts) when the document is no
    /// longer fully active: it is destroyed, or it enters the back/forward cache.
    /// </summary>
    internal void AddPromptCloser(Action close)
    {
        lock (_promptClosersLock)
        {
            (_promptClosers ??= []).Add(close);
        }
    }

    /// <summary>Unregisters the steps of <see cref="AddPromptCloser"/>, once the prompt is closed. Can be called from any thread.</summary>
    internal void RemovePromptCloser(Action close)
    {
        lock (_promptClosersLock)
        {
            _promptClosers?.Remove(close);
        }
    }

    /// <summary>Closes the prompts of the user agent shown by the document, when it is no longer fully active.</summary>
    internal void ClosePrompts()
    {
        List<Action>? closers;
        lock (_promptClosersLock)
        {
            closers = _promptClosers;
            _promptClosers = null;
        }

        if (closers is null)
            return;

        foreach (var close in closers)
        {
            close();
        }
    }
}
