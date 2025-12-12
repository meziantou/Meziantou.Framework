using System.Collections;

namespace Meziantou.Framework;

// https://urlpattern.spec.whatwg.org/#urlpatternlist

/// <summary>Represents a collection of URL patterns that can be matched against URLs.</summary>
/// <remarks>
/// <para>This is the .NET equivalent of the URLPatternList concept from the spec.</para>
/// <para>Use this class when you need to match a URL against multiple patterns.</para>
/// <see href="https://urlpattern.spec.whatwg.org/">WHATWG URL Pattern Spec</see>
/// </remarks>
public sealed class UrlPatternCollection : IReadOnlyList<UrlPattern>
{
    private readonly List<UrlPattern> _patterns = [];

    /// <summary>
    /// Initializes a new empty <see cref="UrlPatternCollection"/>.
    /// </summary>
    public UrlPatternCollection()
    {
    }

    /// <summary>
    /// Initializes a new <see cref="UrlPatternCollection"/> with the specified patterns.
    /// </summary>
    /// <param name="patterns">The patterns to add to the collection.</param>
    public UrlPatternCollection(IEnumerable<UrlPattern> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        _patterns.AddRange(patterns);
    }

    /// <summary>Gets the number of patterns in the collection.</summary>
    public int Count => _patterns.Count;

    /// <summary>Gets the pattern at the specified index.</summary>
    /// <param name="index">The index of the pattern to get.</param>
    /// <returns>The pattern at the specified index.</returns>
    public UrlPattern this[int index] => _patterns[index];

    /// <summary>Adds a pattern to the collection.</summary>
    /// <param name="pattern">The pattern to add.</param>
    public void Add(UrlPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        _patterns.Add(pattern);
    }

    /// <summary>Adds a pattern created from a pattern string.</summary>
    /// <param name="pattern">The pattern string.</param>
    /// <returns>The created and added pattern.</returns>
    public UrlPattern Add(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var urlPattern = UrlPattern.Create(pattern);
        _patterns.Add(urlPattern);
        return urlPattern;
    }

    /// <summary>Adds a pattern created from a pattern string and base URL.</summary>
    /// <param name="pattern">The pattern string.</param>
    /// <param name="baseUrl">The base URL.</param>
    /// <returns>The created and added pattern.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings")]
    public UrlPattern Add(string pattern, string baseUrl)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(baseUrl);
        var urlPattern = UrlPattern.Create(pattern, baseUrl);
        _patterns.Add(urlPattern);
        return urlPattern;
    }

    /// <summary>Adds a pattern created from a URL pattern init.</summary>
    /// <param name="init">The pattern init.</param>
    /// <returns>The created and added pattern.</returns>
    public UrlPattern Add(UrlPatternInit init)
    {
        ArgumentNullException.ThrowIfNull(init);
        var urlPattern = UrlPattern.Create(init);
        _patterns.Add(urlPattern);
        return urlPattern;
    }

    /// <summary>Removes a pattern from the collection.</summary>
    /// <param name="pattern">The pattern to remove.</param>
    /// <returns><see langword="true"/> if the pattern was removed; otherwise, <see langword="false"/>.</returns>
    public bool Remove(UrlPattern pattern)
    {
        return _patterns.Remove(pattern);
    }

    /// <summary>Removes all patterns from the collection.</summary>
    public void Clear()
    {
        _patterns.Clear();
    }

    /// <summary>Determines whether the collection contains the specified pattern.</summary>
    /// <param name="pattern">The pattern to locate.</param>
    /// <returns><see langword="true"/> if the pattern is found; otherwise, <see langword="false"/>.</returns>
    public bool Contains(UrlPattern pattern)
    {
        return _patterns.Contains(pattern);
    }

    /// <summary>Indicates whether any pattern in the collection finds a match in the specified URL.</summary>
    /// <param name="url">The URL to test.</param>
    /// <returns><see langword="true"/> if any pattern matches; otherwise, <see langword="false"/>.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings")]
    public bool IsMatch(string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        foreach (var pattern in _patterns)
        {
            if (pattern.IsMatch(url))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Indicates whether any pattern in the collection finds a match in the specified URL with a base URL.</summary>
    /// <param name="url">The URL to test.</param>
    /// <param name="baseUrl">The base URL.</param>
    /// <returns><see langword="true"/> if any pattern matches; otherwise, <see langword="false"/>.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings")]
    public bool IsMatch(string url, string? baseUrl)
    {
        ArgumentNullException.ThrowIfNull(url);

        foreach (var pattern in _patterns)
        {
            if (pattern.IsMatch(url, baseUrl))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Indicates whether any pattern in the collection finds a match in the specified URL.</summary>
    /// <param name="url">The URL to test.</param>
    /// <returns><see langword="true"/> if any pattern matches; otherwise, <see langword="false"/>.</returns>
    public bool IsMatch(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        foreach (var pattern in _patterns)
        {
            if (pattern.IsMatch(url))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Indicates whether any pattern in the collection finds a match in the specified URL input.</summary>
    /// <param name="input">The URL input to test.</param>
    /// <returns><see langword="true"/> if any pattern matches; otherwise, <see langword="false"/>.</returns>
    public bool IsMatch(UrlPatternInit input)
    {
        ArgumentNullException.ThrowIfNull(input);

        foreach (var pattern in _patterns)
        {
            if (pattern.IsMatch(input))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns the first pattern in the collection that matches the given URL.</summary>
    /// <param name="url">The URL to match against.</param>
    /// <returns>The first matching pattern, or <c>null</c> if no pattern matches.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings")]
    public UrlPattern? FindPattern(string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        foreach (var pattern in _patterns)
        {
            if (pattern.IsMatch(url))
            {
                return pattern;
            }
        }

        return null;
    }

    /// <summary>Returns the first pattern in the collection that matches the given URL with a base URL.</summary>
    /// <param name="url">The URL to match against.</param>
    /// <param name="baseUrl">The base URL.</param>
    /// <returns>The first matching pattern, or <c>null</c> if no pattern matches.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings")]
    public UrlPattern? FindPattern(string url, string? baseUrl)
    {
        ArgumentNullException.ThrowIfNull(url);

        foreach (var pattern in _patterns)
        {
            if (pattern.IsMatch(url, baseUrl))
            {
                return pattern;
            }
        }

        return null;
    }

    /// <summary>Returns the first pattern in the collection that matches the given URL.</summary>
    /// <param name="url">The URL to match against.</param>
    /// <returns>The first matching pattern, or <c>null</c> if no pattern matches.</returns>
    public UrlPattern? FindPattern(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        foreach (var pattern in _patterns)
        {
            if (pattern.IsMatch(url))
            {
                return pattern;
            }
        }

        return null;
    }

    /// <summary>Searches the specified URL using all patterns in the collection and returns the first match result with captured groups.</summary>
    /// <param name="url">The URL to match.</param>
    /// <returns>A <see cref="UrlPatternResult"/> containing the match result, or <see langword="null"/> if no pattern matches.</returns>
    /// <remarks>
    /// <see href="https://urlpattern.spec.whatwg.org/#dom-urlpattern-exec">WHATWG URL Pattern Spec - exec method</see>
    /// </remarks>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings")]
    public UrlPatternResult? Match(string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        foreach (var pattern in _patterns)
        {
            var result = pattern.Match(url);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>Searches the specified URL with a base URL using all patterns in the collection and returns the first match result with captured groups.</summary>
    /// <param name="url">The URL to match.</param>
    /// <param name="baseUrl">The base URL.</param>
    /// <returns>A <see cref="UrlPatternResult"/> containing the match result, or <see langword="null"/> if no pattern matches.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings")]
    public UrlPatternResult? Match(string url, string? baseUrl)
    {
        ArgumentNullException.ThrowIfNull(url);

        foreach (var pattern in _patterns)
        {
            var result = pattern.Match(url, baseUrl);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>Searches the specified URL using all patterns in the collection and returns the first match result with captured groups.</summary>
    /// <param name="url">The URL to match.</param>
    /// <returns>A <see cref="UrlPatternResult"/> containing the match result, or <see langword="null"/> if no pattern matches.</returns>
    public UrlPatternResult? Match(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        foreach (var pattern in _patterns)
        {
            var result = pattern.Match(url);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>Searches the specified URL input using all patterns in the collection and returns the first match result with captured groups.</summary>
    /// <param name="input">The URL input to match.</param>
    /// <returns>A <see cref="UrlPatternResult"/> containing the match result, or <see langword="null"/> if no pattern matches.</returns>
    public UrlPatternResult? Match(UrlPatternInit input)
    {
        ArgumentNullException.ThrowIfNull(input);

        foreach (var pattern in _patterns)
        {
            var result = pattern.Match(input);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public IEnumerator<UrlPattern> GetEnumerator()
    {
        return _patterns.GetEnumerator();
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
