using System.Text.RegularExpressions;
using Meziantou.AspNetCore.Testing.Internals;

namespace Meziantou.AspNetCore.Testing;

/// <summary>
/// The assertions of a page, as the web-first assertions of Playwright: each assertion runs the event loop of the page
/// until it passes, and throws an <see cref="ExpectationFailedException"/> when it still fails after the timeout.
/// </summary>
public sealed class PageAssertions
{
    private readonly TestPage _page;
    private readonly bool _isNot;

    internal PageAssertions(TestPage page, bool isNot)
    {
        _page = page;
        _isNot = isNot;
    }

    /// <summary>Gets the negated assertions (<c>Expect(page).Not.ToHaveUrlAsync("http://localhost/sign-in")</c>).</summary>
    public PageAssertions Not => new(_page, !_isNot);

    /// <summary>Waits until the URL of the active document is a URL.</summary>
    /// <param name="url">The URL, or a path relative to the URL of the document (<c>/profile</c>).</param>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToHaveUrlAsync(string url, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        return WaitAsync("to have the URL " + TextMatcher.Quote(url), () => _page.Url, actual => string.Equals(actual, ResolveUrl(actual, url), StringComparison.Ordinal), timeout, cancellationToken);

        static string ResolveUrl(string baseUrl, string url) => Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) && Uri.TryCreate(baseUri, url, out var absoluteUri) ? absoluteUri.AbsoluteUri : url;
    }

    /// <summary>Waits until the URL of the active document matches a regular expression.</summary>
    /// <param name="url">The regular expression.</param>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToHaveUrlAsync(Regex url, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        return WaitAsync("to have a URL that matches /" + url + "/", () => _page.Url, url.IsMatch, timeout, cancellationToken);
    }

    /// <summary>Waits until the title of the active document is a text, once the white space of both texts is collapsed.</summary>
    /// <param name="title">The title.</param>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToHaveTitleAsync(string title, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(title);
        var normalizedTitle = TextMatcher.NormalizeWhiteSpace(title);
        return WaitAsync("to have the title " + TextMatcher.Quote(normalizedTitle), () => TextMatcher.NormalizeWhiteSpace(_page.Document.Title), actual => string.Equals(actual, normalizedTitle, StringComparison.Ordinal), timeout, cancellationToken);
    }

    /// <summary>Waits until the title of the active document matches a regular expression.</summary>
    /// <param name="title">The regular expression.</param>
    /// <param name="timeout">The maximum time to wait, or null for <see cref="TestPage.DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the assertion passes.</returns>
    public Task ToHaveTitleAsync(Regex title, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(title);
        return WaitAsync("to have a title that matches /" + title + "/", () => _page.Document.Title, title.IsMatch, timeout, cancellationToken);
    }

    private async Task WaitAsync(string expectation, Func<string> getActual, Func<string, bool> predicate, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var actual = "";
        try
        {
            await _page.WaitForAsync(() =>
            {
                actual = getActual();
                return predicate(actual) != _isNot;
            }, timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new ExpectationFailedException($"Expected the page {(_isNot ? "not " : "")}{expectation}, but it is {TextMatcher.Quote(actual)} (waited {timeout ?? _page.DefaultTimeout})", ex);
        }
    }
}
