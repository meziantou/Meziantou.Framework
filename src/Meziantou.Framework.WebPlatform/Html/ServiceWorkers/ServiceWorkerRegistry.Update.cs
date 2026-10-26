using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

// The Update algorithm (https://w3c.github.io/ServiceWorker/#update-algorithm): the script of the service worker is fetched,
// compared byte for byte with the script of the newest worker (and its imported scripts), and a new worker is installed
// when a script changed. The update via cache mode sets the cache mode of the requests, which the HTTP cache uses.
internal sealed partial class ServiceWorkerRegistry
{
    /// <summary>https://w3c.github.io/ServiceWorker/#update-algorithm</summary>
    private async Task UpdateAsync(ServiceWorkerJob job)
    {
        if (GetRegistration(job.StorageKey, job.ScopeUrl) is not { } registration)
        {
            RejectJobPromise(job, static () => new TypeErrorException("The service worker registration does not exist"));
            FinishJob(job);
            return;
        }

        var scriptUrl = job.ScriptUrl!;
        ServiceWorkerRecord? newestWorker;
        bool isStale;
        lock (_lock)
        {
            newestWorker = registration.NewestWorker;
            isStale = registration.IsStale(Now);
        }

        // As in the browsers, the scripts are fetched with the update via cache mode of the job: a register() that changes
        // the mode (e.g. to "none") fetches the scripts with the new mode
        var updateViaCache = job.UpdateViaCache;

        if (job.Type == ServiceWorkerJobType.Update && newestWorker is not null && newestWorker.ScriptUrl.Serialize() != scriptUrl.Serialize())
        {
            RejectJobPromise(job, static () => new TypeErrorException("The script URL of the service worker changed"));
            FinishJob(job);
            return;
        }

        // The main script ("the perform the fetch hook" of the Update algorithm)
        var updatedResourceMap = new OrderedDictionary<string, StoredServiceWorkerScript>(StringComparer.Ordinal);
        var url = scriptUrl.Serialize();
        var request = CreateScriptRequest(job, registration, scriptUrl, isMainScript: true, noCache: updateViaCache != ServiceWorkerUpdateViaCache.All || job.ForceBypassCache || (newestWorker is not null && isStale));
        var response = await FetchScriptAsync(request).ConfigureAwait(false);
        StoredServiceWorkerScript? script = null;
        if (response.Type == FetchResponseType.OpaqueRedirect)
        {
            // The specification fetches the script with the redirect mode "error" (a network error, so a TypeError); the
            // browsers reject the registration of a redirected script with a SecurityError (and an update with a TypeError)
            if (job.Type == ServiceWorkerJobType.Register)
            {
                RejectJobPromise(job, static () => SecurityError("The script of a service worker must not be redirected"));
            }
        }
        else if (!response.IsNetworkError)
        {
            if (HeaderList.ExtractMimeType(response.HeaderList) is not { IsJavaScript: true })
            {
                RejectJobPromise(job, static () => SecurityError("The MIME type of the script of a service worker must be a JavaScript MIME type"));
            }
            else if (!IsScopeAllowed(registration.ScopeUrl, scriptUrl, HeaderList.Get(response.HeaderList, "Service-Worker-Allowed"), out var networkError))
            {
                if (!networkError)
                {
                    RejectJobPromise(job, static () => SecurityError("The scope of the service worker is not allowed by the path of its script (or its Service-Worker-Allowed header)"));
                }
            }
            else
            {
                lock (_lock)
                {
                    registration.LastUpdateCheckTime = Now;
                }

                // https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-classic-worker-script: the status must be ok
                if (response.Status is >= 200 and <= 299)
                {
                    script = WorkerScriptFetching.ToStoredScript(url, response);
                }
            }
        }

        if (script is null)
        {
            RejectJobPromise(job, static () => new TypeErrorException("The script of the service worker could not be fetched"));
            if (newestWorker is null)
            {
                RemoveRegistration(registration);
            }

            FinishJob(job);
            return;
        }

        updatedResourceMap[url] = script;
        StoredServiceWorkerScript? newestScript = null;
        List<KeyValuePair<string, StoredServiceWorkerScript>>? newestImports = null;
        var newestClassicScriptsImported = false;
        if (newestWorker is not null)
        {
            lock (_lock)
            {
                newestWorker.ScriptResourceMap.TryGetValue(url, out newestScript);
                newestImports = [.. newestWorker.ScriptResourceMap];
                newestClassicScriptsImported = newestWorker.ClassicScriptsImported;
            }
        }

        var hasUpdatedResources = newestWorker is null
            || newestWorker.ScriptUrl.Serialize() != url
            || newestWorker.Type != job.WorkerType
            || newestScript is null
            || !newestScript.Body.Span.SequenceEqual(script.Body.Span);

        // The main script did not change: the imported scripts are fetched to check whether they changed. The specification
        // only checks the scripts of importScripts(); the browsers also check the static imports of a module service worker.
        if (!hasUpdatedResources && (newestClassicScriptsImported || job.WorkerType == WorkerType.Module))
        {
            foreach (var (importUrl, storedResponse) in newestImports!)
            {
                if (importUrl == url || UrlParser.Parse(importUrl, baseUrl: null) is not { } importUrlRecord)
                    continue;

                var importRequest = CreateScriptRequest(job, registration, importUrlRecord, isMainScript: false, noCache: updateViaCache == ServiceWorkerUpdateViaCache.None || job.ForceBypassCache || isStale);
                var fetchedResponse = await FetchScriptAsync(importRequest).ConfigureAwait(false);
                lock (_lock)
                {
                    registration.LastUpdateCheckTime = Now;
                }

                // The response is stored even when it is a bad import script response: the new worker (installed because
                // another script changed) fails to import it, rather than fetching it again
                var fetchedScript = WorkerScriptFetching.ToStoredScript(importUrl, fetchedResponse);
                updatedResourceMap[importUrl] = fetchedScript;
                if (WorkerScriptFetching.IsBadImportScriptResponse(fetchedResponse))
                    continue;

                if (!fetchedScript.Body.Span.SequenceEqual(storedResponse.Body.Span))
                {
                    hasUpdatedResources = true;
                }
            }
        }

        if (!hasUpdatedResources)
        {
            lock (_lock)
            {
                registration.UpdateViaCache = job.UpdateViaCache;
            }

            Persist(registration);
            ResolveJobPromise(job, registration);
            FinishJob(job);
            return;
        }

        var worker = new ServiceWorkerRecord(registration, CreateWorkerId(), scriptUrl, job.WorkerType) { InstallingUpdateViaCache = updateViaCache };
        foreach (var (key, value) in updatedResourceMap)
        {
            worker.ScriptResourceMap[key] = value;
        }

        worker.UsedScripts.Add(url);
        worker.UpdateJobClient = job.Client?.EnvironmentDocument;
        var runResult = await RunServiceWorkerAsync(worker).ConfigureAwait(false);
        if (runResult != ServiceWorkerStartStatus.Normal)
        {
            TerminateServiceWorker(worker);
            RejectJobPromise(job, static () => new TypeErrorException("The script of the service worker threw an exception"));
            if (newestWorker is null)
            {
                RemoveRegistration(registration);
            }

            FinishJob(job);
            return;
        }

        await InstallAsync(job, worker, registration).ConfigureAwait(false);
    }

    /// <summary>
    /// The request of a script of a service worker: the main script (https://w3c.github.io/ServiceWorker/#update-algorithm,
    /// the perform the fetch hook), or an imported script of the byte check. A job without client (a soft update) fetches
    /// with the browsing context group (its loader and its cookies), and the origin of the registration.
    /// </summary>
    private FetchRequest CreateScriptRequest(ServiceWorkerJob job, ServiceWorkerRegistrationRecord registration, UrlRecord url, bool isMainScript, bool noCache)
    {
        var client = job.Client?.EnvironmentDocument;
        var origin = client?.SerializeOrigin() ?? registration.ScopeUrl.SerializeOrigin();
        var isModule = job.WorkerType == WorkerType.Module;
        var request = new FetchRequest(url)
        {
            Client = client,
            KeepaliveBrowsingContext = client is null ? GroupOwner : null,
            CookieContext = client is null ? new FetchCookieContext(GroupOwner.GetCookieJar(credentialless: false), origin, origin) : null,
            Origin = origin,
            Referrer = client is null ? "no-referrer" : "client",
            // The module graph of a module service worker is fetched with the credentials mode "omit", and its static imports
            // are CORS requests whose destination is the one of the worker
            // (https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-module-worker-script-tree)
            Destination = isMainScript || isModule ? RequestDestination.ServiceWorker : RequestDestination.Script,
            Mode = isMainScript ? RequestMode.SameOrigin : isModule ? RequestMode.Cors : RequestMode.NoCors,
            CredentialsMode = isModule ? FetchCredentialsMode.Omit : isMainScript ? FetchCredentialsMode.SameOrigin : FetchCredentialsMode.Include,
            // The redirect of the main script is an error (see UpdateAsync, which tells it from a network error)
            RedirectMode = isMainScript ? FetchRedirectMode.Manual : FetchRedirectMode.Follow,
            CacheMode = noCache ? "no-cache" : "default",
            ServiceWorkersMode = ServiceWorkersMode.None,
        };

        if (isMainScript)
        {
            request.HeaderList.Add(new("Service-Worker", "script"));
        }

        return request;
    }

    private static async Task<FetchResponse> FetchScriptAsync(FetchRequest request)
    {
        try
        {
            return await Fetching.FetchAsync(request, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A failure of the fetch is a network error
        catch (Exception)
#pragma warning restore CA1031
        {
            return FetchResponse.NetworkError();
        }
    }

    /// <summary>
    /// The scope check of the Update algorithm: the path of the scope must start with the path of the directory of the
    /// script, or with the path of the <c>Service-Worker-Allowed</c> header (https://w3c.github.io/ServiceWorker/#service-worker-allowed).
    /// <paramref name="networkError"/> is true when the header is not a valid URL.
    /// </summary>
    private static bool IsScopeAllowed(UrlRecord scopeUrl, UrlRecord scriptUrl, string? serviceWorkerAllowed, out bool networkError)
    {
        networkError = false;
        string? maxScopeString = null;
        if (serviceWorkerAllowed is null)
        {
            var resolvedScope = UrlParser.Parse("./", scriptUrl)!;
            maxScopeString = "/" + string.Join('/', resolvedScope.Path);
        }
        else
        {
            if (UrlParser.Parse(serviceWorkerAllowed, scriptUrl) is not { } maxScope)
            {
                networkError = true;
                return false;
            }

            if (maxScope.SerializeOrigin() == scriptUrl.SerializeOrigin())
            {
                maxScopeString = "/" + string.Join('/', maxScope.Path);
            }
        }

        var scopeString = "/" + string.Join('/', scopeUrl.Path);
        return maxScopeString is not null && scopeString.StartsWith(maxScopeString, StringComparison.Ordinal);
    }
}
