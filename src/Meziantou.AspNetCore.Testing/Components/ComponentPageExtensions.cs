using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Components;

/// <summary>Waits for the interactivity of the Blazor components of a page of an application (see <see cref="WebAppBrowser"/>).</summary>
public static class ComponentPageExtensions
{
    /// <summary>
    /// Runs the event loop of a page until its interactive components are attached: <c>blazor.web.js</c> replaces the
    /// markers of the prerendered components (<c>&lt;!--bl-root--&gt;</c> since .NET 11, <c>&lt;!--Blazor:{...}--&gt;</c>
    /// before) once their circuit (Interactive Server) or their runtime has started and rendered them. Before that, the
    /// events of the prerendered elements reach no component.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the components are interactive.</returns>
    /// <exception cref="TimeoutException">The components are not interactive after <paramref name="timeout"/>.</exception>
    public static Task WaitForInteractivityAsync(this TestPage page, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        return page.WaitForAsync(() => !HasPendingComponentMarkers(page.Document), timeout, cancellationToken);
    }

    private static bool HasPendingComponentMarkers(Node node)
    {
        for (var child = node.FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is Comment comment && IsPendingComponentMarker(comment.Data))
                return true;

            if (HasPendingComponentMarkers(child))
                return true;
        }

        return false;
    }

    private static bool IsPendingComponentMarker(string data)
    {
        if (data == "bl-root")
            return true;

        return data.StartsWith("Blazor:{", StringComparison.Ordinal)
            && (data.Contains("\"type\":\"server\"", StringComparison.Ordinal) || data.Contains("\"type\":\"webassembly\"", StringComparison.Ordinal) || data.Contains("\"type\":\"auto\"", StringComparison.Ordinal));
    }
}
