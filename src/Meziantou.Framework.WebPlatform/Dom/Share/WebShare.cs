using Meziantou.Framework.WebPlatform.Dom.FileApi;
using File = Meziantou.Framework.WebPlatform.Dom.FileApi.File;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The data to share with <c>navigator.share()</c> (https://w3c.github.io/web-share/#sharedata-dictionary). A null member is not present.</summary>
/// <seealso href="https://w3c.github.io/web-share/#dom-sharedata"/>
[IdlInterface("ShareData", "https://w3c.github.io/web-share/#dom-sharedata")]
public sealed class ShareData
{
    /// <summary>Gets the files to share.</summary>
    /// <seealso href="https://w3c.github.io/web-share/#dom-sharedata-files"/>
    [IdlMember("files", "https://w3c.github.io/web-share/#dom-sharedata-files")]
    public IReadOnlyList<File>? Files { get; init; }

    /// <summary>Gets the title of the shared content.</summary>
    /// <seealso href="https://w3c.github.io/web-share/#dom-sharedata-title"/>
    [IdlMember("title", "https://w3c.github.io/web-share/#dom-sharedata-title")]
    public string? Title { get; init; }

    /// <summary>Gets the text to share.</summary>
    /// <seealso href="https://w3c.github.io/web-share/#dom-sharedata-text"/>
    [IdlMember("text", "https://w3c.github.io/web-share/#dom-sharedata-text")]
    public string? Text { get; init; }

    /// <summary>Gets the URL to share, relative to the base URL of the document.</summary>
    /// <seealso href="https://w3c.github.io/web-share/#dom-sharedata-url"/>
    [IdlMember("url", "https://w3c.github.io/web-share/#dom-sharedata-url")]
    public string? Url { get; init; }
}

/// <summary>
/// The validated data of a <c>navigator.share()</c> call, given to <see cref="BrowsingContextOptions.Share"/>. A null member was
/// not given by the script.
/// </summary>
public sealed class ShareRequest
{
    internal ShareRequest(string? title, string? text, string? url, IReadOnlyList<File> files)
    {
        Title = title;
        Text = text;
        Url = url;
        Files = files;
    }

    /// <summary>Gets the title of the shared content, or null.</summary>
    public string? Title { get; }

    /// <summary>Gets the text to share, or null.</summary>
    public string? Text { get; }

    /// <summary>Gets the absolute URL to share (resolved against the base URL of the document and serialized), or null.</summary>
    public string? Url { get; }

    /// <summary>Gets the files to share (empty when there is none).</summary>
    public IReadOnlyList<File> Files { get; }
}

// The Web Share API (https://w3c.github.io/web-share/)
public sealed partial class Navigator
{
    private bool _isSharing;

    /// <summary>
    /// Shares data with a target chosen by the user (https://w3c.github.io/web-share/#share-method), with
    /// <see cref="BrowsingContextOptions.Share"/>. The task fails with an <c>InvalidStateError</c> <see cref="DomException"/>
    /// when the document is not fully active or a share is in progress, a <c>NotAllowedError</c> when the document is not
    /// allowed to use the <c>web-share</c> feature or has no transient activation, a <see cref="TypeErrorException"/> when the
    /// data is not valid, and an <c>AbortError</c> when there is no share target or the user cancels.
    /// </summary>
    /// <param name="data">The data to share.</param>
    /// <seealso href="https://w3c.github.io/web-share/#dom-navigator-share"/>
    [IdlMember("share", "https://w3c.github.io/web-share/#dom-navigator-share")]
    public Task Share(ShareData? data = null)
    {
        data ??= new ShareData();
        var window = _window;
        var document = window.Document;
        if (!document.IsFullyActive)
            return Task.FromException(DomException.InvalidState("The document is not fully active"));

        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.WebShare))
            return Task.FromException(DomException.NotAllowed("The document is not allowed to use the web-share feature"));

        if (_isSharing)
            return Task.FromException(DomException.InvalidState("A share is already in progress"));

        if (!window.HasTransientActivation)
            return Task.FromException(DomException.NotAllowed("navigator.share() requires a user activation"));

        window.ConsumeUserActivation();
        if (ValidateShareData(data, document.DocumentBaseUrl) is not { } request)
            return Task.FromException(new TypeErrorException("The share data is not valid"));

        _isSharing = true;
        var completion = new TaskCompletionSource();
        if (window.Options.Share is not { } share)
        {
            // There is no share target: the promise is rejected by a task (https://w3c.github.io/web-share/#share-method, step 13.1)
            window.QueueGlobalTask(() =>
            {
                _isSharing = false;
                completion.TrySetException(new DomException("There is no share target", "AbortError"));
            });
            return completion.Task;
        }

        ValueTask<bool> result;
        try
        {
            result = share(CallbackContext.For(window), request);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _isSharing = false;
            return Task.FromException(new DomException("The share failed: " + ex.Message, "DataError"));
        }

        if (result.IsCompleted)
        {
            Complete(result.AsTask());
            return completion.Task;
        }

        var eventLoop = window.EventLoop;
        eventLoop.BeginOperation();
        _ = result.AsTask().ContinueWith(
            completed =>
            {
                Complete(completed);
                eventLoop.EndOperation();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return completion.Task;

        void Complete(Task<bool> completed)
        {
            // The promise is settled in a task (https://w3c.github.io/web-share/#share-method, step 10)
            window.QueueGlobalTask(() =>
            {
                _isSharing = false;
                if (completed.IsCompletedSuccessfully)
                {
                    if (completed.Result)
                    {
                        completion.TrySetResult();
                    }
                    else
                    {
                        completion.TrySetException(new DomException("The share was canceled", "AbortError"));
                    }
                }
                else if (completed.IsCanceled)
                {
                    completion.TrySetException(new DomException("The share was canceled", "AbortError"));
                }
                else
                {
                    completion.TrySetException(new DomException("The share failed: " + completed.Exception?.GetBaseException().Message, "DataError"));
                }
            });
        }
    }

    /// <summary>
    /// Returns whether <see cref="Share"/> would accept the data (https://w3c.github.io/web-share/#canshare-method): false when
    /// the document is not fully active, is not allowed to use the <c>web-share</c> feature, or the data is not valid.
    /// </summary>
    /// <param name="data">The data to share.</param>
    /// <seealso href="https://w3c.github.io/web-share/#dom-navigator-canshare"/>
    [IdlMember("canShare", "https://w3c.github.io/web-share/#dom-navigator-canshare")]
    public bool CanShare(ShareData? data = null)
    {
        var document = _window.Document;
        if (!document.IsFullyActive || !PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.WebShare, report: false))
            return false;

        return ValidateShareData(data ?? new ShareData(), document.DocumentBaseUrl) is not null;
    }

    /// <summary>https://w3c.github.io/web-share/#dfn-validate-share-data: the validated data, or null when it is not valid.</summary>
    private static ShareRequest? ValidateShareData(ShareData data, UrlRecord baseUrl)
    {
        if (data.Title is null && data.Text is null && data.Url is null && data.Files is null)
            return null;

        var titleTextOrUrl = data.Title is not null || data.Text is not null || data.Url is not null;
        if (data.Files is { Count: 0 } && !titleTextOrUrl)
            return null;

        string? url = null;
        if (data.Url is not null)
        {
            // The local schemes, file, javascript, ws and wss are not shared, and the other schemes must be sharable schemes
            if (UrlParser.Parse(data.Url, baseUrl) is not { } parsed || parsed.Scheme is "about" or "blob" or "data" or "file" or "javascript" or "ws" or "wss")
                return null;

            if (parsed.Scheme is not ("http" or "https") && !IsSafelistedScheme(parsed.Scheme))
                return null;

            url = parsed.Serialize();
        }

        return new ShareRequest(data.Title, data.Text, url, data.Files?.ToArray() ?? []);
    }
}
