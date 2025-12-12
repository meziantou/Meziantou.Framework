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
    /// <returns><c>true</c> if the pattern was removed; otherwise, <c>false</c>.</returns>
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
    /// <returns><c>true</c> if the pattern is found; otherwise, <c>false</c>.</returns>
    public bool Contains(UrlPattern pattern)
    {
        return _patterns.Contains(pattern);
    }

    /// <summary>Tests if any pattern in the collection matches the given URL.</summary>
    /// <param name="url">The URL to test.</param>
    /// <returns><c>true</c> if any pattern matches; otherwise, <c>false</c>.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings")]
    public bool Test(string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        foreach (var pattern in _patterns)
        {
            if (pattern.Test(url))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Tests if any pattern in the collection matches the given URL with a base URL.</summary>
    /// <param name="url">The URL to test.</param>
    /// <param name="baseUrl">The base URL.</param>
    /// <returns><c>true</c> if any pattern matches; otherwise, <c>false</c>.</returns>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings")]
    public bool Test(string url, string? baseUrl)
    {
        ArgumentNullException.ThrowIfNull(url);

        foreach (var pattern in _patterns)
        {
            if (pattern.Test(url, baseUrl))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Tests if any pattern in the collection matches the given URL.</summary>
    /// <param name="url">The URL to test.</param>
    /// <returns><c>true</c> if any pattern matches; otherwise, <c>false</c>.</returns>
    public bool Test(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        foreach (var pattern in _patterns)
        {
            if (pattern.Test(url))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Tests if any pattern in the collection matches the given URL input.</summary>
    /// <param name="input">The URL input to test.</param>
    /// <returns><c>true</c> if any pattern matches; otherwise, <c>false</c>.</returns>
    public bool Test(UrlPatternInit input)
    {
        ArgumentNullException.ThrowIfNull(input);

        foreach (var pattern in _patterns)
        {
            if (pattern.Test(input))
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
    public UrlPattern? Match(string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        foreach (var pattern in _patterns)
        {
            if (pattern.Test(url))
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
    public UrlPattern? Match(string url, string? baseUrl)
    {
        ArgumentNullException.ThrowIfNull(url);

        foreach (var pattern in _patterns)
        {
            if (pattern.Test(url, baseUrl))
            {
                return pattern;
            }
        }

        return null;
    }

    /// <summary>Returns the first pattern in the collection that matches the given URL.</summary>
    /// <param name="url">The URL to match against.</param>
    /// <returns>The first matching pattern, or <c>null</c> if no pattern matches.</returns>
    public UrlPattern? Match(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        foreach (var pattern in _patterns)
        {
            if (pattern.Test(url))
            {
                return pattern;
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
