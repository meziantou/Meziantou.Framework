namespace Meziantou.Framework.WebPlatform.Dom;

// The Storage Access API (https://privacycg.github.io/storage-access/): a document in a third-party context queries and
// requests the access to its unpartitioned cookies.
public partial class Document
{
    /// <summary>
    /// https://privacycg.github.io/storage-access/#environment-has-storage-access: whether the environment of the document
    /// has storage access. It is set by <see cref="RequestStorageAccess"/>, and inherited by the document of a navigation
    /// that the document initiates in its own navigable to its own origin (e.g. a reload), or whose response has the
    /// <c>Activate-Storage-Access: load</c> header.
    /// </summary>
    internal bool EnvironmentHasStorageAccess { get; set; }

    /// <summary>
    /// Returns whether the document has access to its unpartitioned cookies
    /// (https://privacycg.github.io/storage-access/#dom-document-hasstorageaccess): true in a first-party context (a top-level
    /// document, or a document that is same site with its top-level document and its ancestors) or when the user allows
    /// the unpartitioned cookies of its site (<see cref="CookieJar.SetThirdPartyCookieAccess"/>); in a third-party context,
    /// whether the document obtained storage access (<see cref="RequestStorageAccess"/>) and the <c>storage-access</c>
    /// permission is still granted. The task fails with an <c>InvalidStateError</c> <see cref="DomException"/> when the
    /// document is not fully active, and is false for a document of an opaque origin, that is not a secure context, or that
    /// is loaded in a credentialless iframe.
    /// </summary>
    /// <seealso href="https://privacycg.github.io/storage-access/#dom-document-hasstorageaccess"/>
    [IdlMember("hasStorageAccess", "https://privacycg.github.io/storage-access/#dom-document-hasstorageaccess")]
    public Task<bool> HasStorageAccess()
    {
        if (!IsFullyActive || BrowsingContext is not { } browsingContext || RelevantWindow is not { } global)
            return Task.FromException<bool>(DomException.InvalidState("The document is not fully active"));

        // A credentialless document (https://wicg.github.io/anonymous-iframe/#spec-storage-access-api) uses an ephemeral cookie jar
        if (Origin.IsOpaque || !global.IsSecureContext || IsCredentialless)
            return Task.FromResult(false);

        var topLevel = TopLevelOrigin;
        if (topLevel.IsOpaque)
            return Task.FromResult(false);

        // The settings of the user and the state of the permission are read in parallel
        var embedded = Origin;
        var explicitlyAllowed = CookieJar?.ExplicitlyAllowsUnpartitionedCookieAccess(topLevel, embedded) == true;
        var permissionState = StorageAccess.GetPermissionState(EnvironmentOptions?.EffectivePermissions, topLevel, embedded);
        var result = new TaskCompletionSource<bool>();
        QueueStorageAccessTask(browsingContext, global, () =>
        {
            if (explicitlyAllowed || browsingContext.Parent is null || StorageAccess.IsFirstPartySiteContext(this))
            {
                result.TrySetResult(true);
            }
            else if (OriginRecord.IsSameSite(embedded, topLevel) || permissionState == PermissionState.Granted)
            {
                result.TrySetResult(EnvironmentHasStorageAccess);
            }
            else
            {
                result.TrySetResult(false);
            }
        });
        return result.Task;
    }

    /// <summary>
    /// Queues a global task on the networking task source of the window: as the window of an initial <c>about:blank</c>
    /// document is reused by the next document, the task runs when the current document of the window is fully active.
    /// </summary>
    private static void QueueStorageAccessTask(BrowsingContext browsingContext, Window global, Action task)
        => browsingContext.EventLoop.QueueTask(() => global.Document.RunGlobalTask(task));

    /// <summary>
    /// Requests the access to the unpartitioned cookies of the document (https://privacycg.github.io/storage-access/#dom-document-requeststorageaccess):
    /// the request is granted in a first-party context, when the user allows the unpartitioned cookies of the site, when
    /// the <c>storage-access</c> permission is granted, or when the document has transient activation and the user grants
    /// the permission (<see cref="BrowsingContextOptions.RequestPermission"/>). The environment of the document then has
    /// storage access: its scripts and its same-origin requests get the unpartitioned cookies, even when the cookie jar
    /// blocks the third-party cookies (<see cref="CookieJar.BlockThirdPartyCookies"/>). A denied request consumes the user
    /// activation. The task fails with an <c>InvalidStateError</c> <see cref="DomException"/> when the document is not fully
    /// active, and with a <c>NotAllowedError</c> <see cref="DomException"/> when the request is denied, or when the document
    /// is not a secure context, is not allowed to use the <c>storage-access</c> feature by its permissions policy, has an
    /// opaque origin, is sandboxed without the <c>allow-storage-access-by-user-activation</c> keyword, or is loaded in a
    /// credentialless iframe.
    /// </summary>
    /// <seealso href="https://privacycg.github.io/storage-access/#dom-document-requeststorageaccess"/>
    [IdlMember("requestStorageAccess", "https://privacycg.github.io/storage-access/#dom-document-requeststorageaccess")]
    public Task RequestStorageAccess()
    {
        if (!IsFullyActive || BrowsingContext is not { } browsingContext || RelevantWindow is not { } global)
            return Task.FromException(DomException.InvalidState("The document is not fully active"));

        if (!global.IsSecureContext)
            return Task.FromException(DomException.NotAllowed("The document is not a secure context"));

        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(this, PolicyControlledFeature.StorageAccess))
            return Task.FromException(DomException.NotAllowed("The document is not allowed to use the 'storage-access' feature"));

        var topLevel = TopLevelOrigin;
        if (Origin.IsOpaque || topLevel.IsOpaque)
            return Task.FromException(DomException.NotAllowed("The document has an opaque origin"));

        if ((ActiveSandboxingFlags & SandboxingFlags.StorageAccessByUserActivation) != SandboxingFlags.None)
            return Task.FromException(DomException.NotAllowed("The document is sandboxed without the 'allow-storage-access-by-user-activation' keyword"));

        // A credentialless document (https://wicg.github.io/anonymous-iframe/#spec-storage-access-api) cannot get the unpartitioned cookies
        if (IsCredentialless)
            return Task.FromException(DomException.NotAllowed("The document is loaded in a credentialless iframe"));

        var embedded = Origin;
        var hasTransientActivation = global.HasTransientActivation;
        var result = new TaskCompletionSource();

        // The permission is determined in parallel, and the promise is settled by a task
        if (CookieJar?.ExplicitlyAllowsUnpartitionedCookieAccess(topLevel, embedded) == true || browsingContext.Parent is null || OriginRecord.IsSameSite(embedded, topLevel))
        {
            ProcessPermissionState(PermissionState.Granted);
        }
        else if (StorageAccess.GetPermissionState(EnvironmentOptions?.EffectivePermissions, topLevel, embedded) is var previousState && previousState != PermissionState.Prompt)
        {
            ProcessPermissionState(previousState);
        }
        else if (!hasTransientActivation)
        {
            ProcessPermissionState(PermissionState.Denied);
        }
        else
        {
            QueueStorageAccessTask(browsingContext, global, () => ProcessPermissionState(Permissions.RequestPermissionToUse(global, StorageAccess.FeatureName)));
        }

        return result.Task;

        void ProcessPermissionState(PermissionState state)
        {
            QueueStorageAccessTask(browsingContext, global, () =>
            {
                if (state == PermissionState.Granted)
                {
                    EnvironmentHasStorageAccess = true;
                    result.TrySetResult();
                }
                else
                {
                    global.ConsumeUserActivation();
                    result.TrySetException(DomException.NotAllowed("The storage access is denied"));
                }
            });
        }
    }
}
