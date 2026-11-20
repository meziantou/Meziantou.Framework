namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>The handlers of the <c>network-error-logging</c> tests (<c>network-error-logging/support/*.py</c>).</summary>
internal static partial class WptPythonHandlers
{
    /// <summary>The stash key of the lock of lock.py (<c>_LOCK_KEY</c>).</summary>
    private const string NetworkErrorLoggingLockKey = "67966d2e-a847-41d8-b7c3-5f6aee3375ba";

    /// <summary>The time lock.py waits for the lock (<c>_TIMEOUT</c>).</summary>
    private static readonly TimeSpan NetworkErrorLoggingLockTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The interval of the polling of lock.py (<c>time.sleep(0.5)</c>).</summary>
    private static readonly TimeSpan NetworkErrorLoggingLockPollingInterval = TimeSpan.FromSeconds(0.5);

    private static readonly Dictionary<string, Handler> NetworkErrorLoggingHandlers = new(StringComparer.Ordinal)
    {
        // lock.py: a lock that serializes the test cases (op=lock waits until the stash has no holder, op=unlock releases it)
        ["/network-error-logging/support/lock.py"] = (request, response, stash) =>
        {
            var reportId = request.Get("reportID") ?? "";
            switch (request.Get("op"))
            {
                case "lock":
                    // wait_for_lock(): the stash is polled every 0.5 second (measured by the virtual clock) for 5 seconds
                    if (request.PollingElapsed < NetworkErrorLoggingLockPollingInterval)
                    {
                        response.RetryAfter = NetworkErrorLoggingLockPollingInterval - request.PollingElapsed;
                        return;
                    }

                    lock (stash)
                    {
                        if (stash.TakeValue(NetworkErrorLoggingLockKey, request.Path) is { } holder)
                        {
                            stash.Put(NetworkErrorLoggingLockKey, holder, request.Path);
                            if (request.PollingElapsed < NetworkErrorLoggingLockTimeout)
                            {
                                response.RetryAfter = NetworkErrorLoggingLockPollingInterval;
                                return;
                            }

                            response.Status = 503;
                            response.Content = "Cannot obtain lock";
                            return;
                        }

                        stash.Put(NetworkErrorLoggingLockKey, reportId, request.Path);
                    }

                    response.Content = "Obtained lock for " + reportId;
                    return;

                case "unlock":
                    lock (stash)
                    {
                        var holder = stash.TakeValue(NetworkErrorLoggingLockKey, request.Path) as string;
                        if (holder != reportId)
                        {
                            if (holder is not null)
                            {
                                stash.Put(NetworkErrorLoggingLockKey, holder, request.Path);
                            }

                            response.Status = 503;
                            response.Content = "Cannot release lock held by " + holder;
                            return;
                        }
                    }

                    response.Content = "Released lock for " + reportId;
                    return;

                default:
                    response.Status = 400;
                    response.Content = "Invalid op";
                    return;
            }
        },

        // redirect.py: always redirects to no-policy-pass.png
        ["/network-error-logging/support/redirect.py"] = (_, response, _) =>
        {
            response.Status = 302;
            response.Add("Location", "no-policy-pass.png");
        },

        // cached-with-validation.py: a response with an ETag, and a 304 response to a conditional request with that ETag
        ["/network-error-logging/support/cached-with-validation.py"] = (request, response, _) =>
        {
            const string ETag = "\"123abc\"";
            if (request.Header("If-None-Match") == ETag)
            {
                response.Set("X-HTTP-STATUS", "304");
                response.Status = 304;
                response.StatusText = "Not Modified";
                return;
            }

            response.Set("ETag", ETag);
            response.Set("Content-Type", "text/plain");
            response.Content = "lorem ipsum dolor sit amet";
        },
    };
}
