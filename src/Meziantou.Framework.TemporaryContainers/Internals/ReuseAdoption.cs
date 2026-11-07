using System.Diagnostics;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>Finds the container another process created with the same reuse identifier, once the runtime refused to create a second one with its name.</summary>
internal static class ReuseAdoption
{
    // A daemon reserves the name of a container as soon as it starts creating it, and only lists the container once it
    // is created: the lookup that follows a refused name finds nothing at all when it runs in that window, so it is
    // tried again for as long as a creation takes. The image is already there by then, so the window is short, and a
    // name that is really taken by a container of somebody else must still be reported quickly.
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LookupDelay = TimeSpan.FromMilliseconds(100);

    public static async Task<string?> FindAsync(Func<CancellationToken, Task<string?>> find, CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            if (await find(cancellationToken).ConfigureAwait(false) is { } containerId)
                return containerId;

            if (elapsed.Elapsed >= LookupTimeout)
                return null;

            await Task.Delay(LookupDelay, cancellationToken).ConfigureAwait(false);
        }
    }
}
