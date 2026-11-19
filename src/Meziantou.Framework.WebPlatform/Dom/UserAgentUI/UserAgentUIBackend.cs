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
    /// prompt is canceled. While the prompt is shown, the event loop is not idle (until the backend answers, or
    /// <paramref name="cancellationToken"/> is canceled: the caller settles its promise itself, and the late answer is ignored).
    /// </summary>
    internal static void Show<T>(Window window, Func<UserAgentUIBackend, CancellationToken, ValueTask<T>> prompt, T dismissed, Action<T> complete, CancellationToken cancellationToken = default)
    {
        if (window.Options.UserAgentUI is not { } backend)
        {
            window.QueueGlobalTask(() => complete(dismissed));
            return;
        }

        ValueTask<T> result;
        try
        {
            result = prompt(backend, cancellationToken);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            window.QueueGlobalTask(() => complete(dismissed));
            return;
        }

        if (result.IsCompleted)
        {
            var answer = GetAnswer(result.AsTask(), dismissed);
            window.QueueGlobalTask(() => complete(answer));
            return;
        }

        var eventLoop = window.EventLoop;
        eventLoop.BeginOperation();
        var ended = 0;
        void End()
        {
            if (Interlocked.Exchange(ref ended, 1) == 0)
            {
                eventLoop.EndOperation();
            }
        }

        var registration = cancellationToken.CanBeCanceled ? cancellationToken.Register(End) : default;
        _ = result.AsTask().ContinueWith(
            completed =>
            {
                registration.Dispose();
                if (Volatile.Read(ref ended) == 0)
                {
                    var answer = GetAnswer(completed, dismissed);
                    window.QueueGlobalTask(() =>
                    {
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            complete(answer);
                        }
                    });
                }

                End();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static T GetAnswer<T>(Task<T> task, T dismissed) => task.IsCompletedSuccessfully ? task.Result : dismissed;
}
