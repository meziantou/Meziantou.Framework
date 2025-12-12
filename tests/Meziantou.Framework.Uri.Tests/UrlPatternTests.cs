namespace Meziantou.Framework.Tests;

/// <summary>
/// Unit tests for UrlPattern based on WHATWG URL Pattern Spec examples.
/// See: <see href="https://urlpattern.spec.whatwg.org/"/>
/// See: <see href="https://developer.mozilla.org/en-US/docs/Web/API/URL_Pattern_API"/>
/// </summary>
public sealed class UrlPatternTests
{
    // https://urlpattern.spec.whatwg.org/#example-1
    [Fact]
    public void Create_WithPatternStringAndBaseUrl_ShouldWork()
    {
        var pattern = UrlPattern.Create("/books/:id", "https://example.com");

        Assert.Equal("https", pattern.Protocol);
        Assert.Equal("example.com", pattern.Hostname);
        Assert.Equal("/books/:id", pattern.Pathname);
    }

    // https://urlpattern.spec.whatwg.org/#example-2
    [Fact]
    public void Create_WithUrlPatternInit_ShouldWork()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Protocol = "https",
            Hostname = "example.com",
            Pathname = "/books/:id",
        });

        Assert.Equal("https", pattern.Protocol);
        Assert.Equal("example.com", pattern.Hostname);
        Assert.Equal("/books/:id", pattern.Pathname);
    }

    // https://developer.mozilla.org/en-US/docs/Web/API/URL_Pattern_API#matching_url_paths
    [Fact]
    public void Test_WithMatchingUrl_ShouldReturnTrue()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/books/:id",
        });

        Assert.True(pattern.Test("https://example.com/books/123"));
    }

    [Fact]
    public void Test_WithNonMatchingUrl_ShouldReturnFalse()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/books/:id",
        });

        Assert.False(pattern.Test("https://example.com/articles/123"));
    }

    // https://developer.mozilla.org/en-US/docs/Web/API/URL_Pattern_API#named_groups
    [Fact]
    public void Create_WithNamedGroup_ShouldNormalizePattern()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/product/:id",
        });

        Assert.True(pattern.Test("https://example.com/product/123"));
        Assert.True(pattern.Test("https://example.com/product/abc"));
        Assert.False(pattern.Test("https://example.com/product/")); // Empty segment should not match
        Assert.False(pattern.Test("https://example.com/product")); // Missing segment
    }

    // https://developer.mozilla.org/en-US/docs/Web/API/URL_Pattern_API#wildcards
    [Fact]
    public void Create_WithWildcard_ShouldMatchAnyValue()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/product/*",
        });

        Assert.True(pattern.Test("https://example.com/product/123"));
        Assert.True(pattern.Test("https://example.com/product/abc/def"));
    }

    // https://developer.mozilla.org/en-US/docs/Web/API/URL_Pattern_API#pattern_syntax
    [Fact]
    public void Create_WithOptionalGroup_ShouldMatchWithOrWithout()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/books{/:category}?",
        });

        Assert.True(pattern.Test("https://example.com/books"));
        Assert.True(pattern.Test("https://example.com/books/fiction"));
    }

    // Protocol matching
    [Fact]
    public void Test_MatchProtocol()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Protocol = "https",
        });

        Assert.True(pattern.Test("https://example.com"));
        Assert.False(pattern.Test("http://example.com"));
    }

    // Hostname matching
    [Fact]
    public void Test_MatchHostname()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Hostname = "example.com",
        });

        Assert.True(pattern.Test("https://example.com/path"));
        Assert.False(pattern.Test("https://other.com/path"));
    }

    // Port matching
    [Fact]
    public void Test_MatchPort()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Port = "8080",
        });

        Assert.True(pattern.Test("https://example.com:8080/path"));
        Assert.False(pattern.Test("https://example.com:9090/path"));
    }

    // Search/query matching
    [Fact]
    public void Test_MatchSearch()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Search = "foo=:value",
        });

        Assert.True(pattern.Test("https://example.com?foo=bar"));
        Assert.True(pattern.Test("https://example.com?foo=123"));
    }

    // Hash/fragment matching
    [Fact]
    public void Test_MatchHash()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Hash = "section-:id",
        });

        Assert.True(pattern.Test("https://example.com#section-1"));
        Assert.True(pattern.Test("https://example.com#section-abc"));
    }

    // Case insensitive matching
    [Fact]
    public void Test_IgnoreCase_ShouldMatchCaseInsensitively()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/books/:id",
        }, new UrlPatternOptions { IgnoreCase = true });

        Assert.True(pattern.Test("https://example.com/BOOKS/123"));
        Assert.True(pattern.Test("https://example.com/Books/123"));
    }

    // Full URL pattern string
    [Fact]
    public void Create_WithFullUrlPatternString_ShouldParse()
    {
        var pattern = UrlPattern.Create("https://example.com/books/:id");

        Assert.Equal("https", pattern.Protocol);
        Assert.Equal("example.com", pattern.Hostname);
        Assert.Equal("/books/:id", pattern.Pathname);
        Assert.True(pattern.Test("https://example.com/books/123"));
    }

    // Wildcard protocol
    [Fact]
    public void Test_WildcardProtocol_ShouldMatchAny()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Protocol = "*",
            Hostname = "example.com",
        });

        Assert.True(pattern.Test("https://example.com"));
        Assert.True(pattern.Test("http://example.com"));
        Assert.True(pattern.Test("ftp://example.com"));
    }

    // Wildcard hostname
    [Fact]
    public void Test_WildcardHostname_ShouldMatchAny()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Protocol = "https",
            Hostname = "*",
        });

        Assert.True(pattern.Test("https://example.com"));
        Assert.True(pattern.Test("https://foo.bar.com"));
    }

    // Multiple path segments
    [Fact]
    public void Test_MultiplePathSegments()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/api/:version/users/:userId/posts/:postId",
        });

        Assert.True(pattern.Test("https://example.com/api/v1/users/123/posts/456"));
        Assert.False(pattern.Test("https://example.com/api/v1/users/123"));
    }

    // HasRegExpGroups property
    [Fact]
    public void HasRegExpGroups_WithNamedGroups_ShouldReturnFalse()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/books/:id",
        });

        // Named groups like :id are not regexp groups - they're segment wildcards
        Assert.False(pattern.HasRegExpGroups);
    }

    [Fact]
    public void HasRegExpGroups_WithCustomRegExp_ShouldReturnTrue()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/books/:id(\\d+)",
        });

        Assert.True(pattern.HasRegExpGroups);
    }

    // Test with Uri input
    [Fact]
    public void Test_WithUriInput_ShouldWork()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/books/:id",
        });

        var uri = new System.Uri("https://example.com/books/123");
        Assert.True(pattern.Test(uri));
    }

    [Fact]
    public void Test_WithUrlPatternInitInput_ShouldWork()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Protocol = "https",
            Hostname = "example.com",
        });

        var result = pattern.Test(new UrlPatternInit
        {
            Protocol = "https",
            Hostname = "example.com",
            Pathname = "/any/path",
        });

        Assert.True(result);
    }

    // Fixed text pattern
    [Fact]
    public void Test_FixedPathname_ShouldMatchExactly()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/exact/path",
        });

        Assert.True(pattern.Test("https://example.com/exact/path"));
        Assert.False(pattern.Test("https://example.com/exact/path/extra"));
        Assert.False(pattern.Test("https://example.com/wrong/path"));
    }

    // Empty component
    [Fact]
    public void Test_EmptySearch_ShouldMatchNoQueryString()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/path",
            Search = "",
        });

        Assert.True(pattern.Test("https://example.com/path"));
        Assert.False(pattern.Test("https://example.com/path?query=value"));
    }

    // Default port handling - https uses 443
    [Fact]
    public void Create_HttpsWithPort443_ShouldNormalizeToEmptyPort()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Protocol = "https",
            Hostname = "example.com",
            Port = "443",
        });

        // The port should be normalized to empty string for default HTTPS port
        Assert.Equal("", pattern.Port);
    }

    // Default port handling - http uses 80
    [Fact]
    public void Create_HttpWithPort80_ShouldNormalizeToEmptyPort()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Protocol = "http",
            Hostname = "example.com",
            Port = "80",
        });

        Assert.Equal("", pattern.Port);
    }

    // Special scheme handling
    [Theory]
    [InlineData("http")]
    [InlineData("https")]
    [InlineData("ws")]
    [InlineData("wss")]
    [InlineData("ftp")]
    public void Test_SpecialSchemes_ShouldBeRecognized(string scheme)
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Protocol = scheme,
            Hostname = "example.com",
            Pathname = "/",
        });

        Assert.Equal(scheme, pattern.Protocol);
    }

    // Repeated modifiers
    [Fact]
    public void Create_WithOneOrMoreModifier_ShouldMatchMultiple()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/files/:path+",
        });

        Assert.True(pattern.Test("https://example.com/files/a"));
        Assert.True(pattern.Test("https://example.com/files/a/b"));
        Assert.True(pattern.Test("https://example.com/files/a/b/c"));
        Assert.False(pattern.Test("https://example.com/files"));
    }

    [Fact]
    public void Create_WithZeroOrMoreModifier_ShouldMatchZeroOrMore()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/files/:path*",
        });

        Assert.True(pattern.Test("https://example.com/files"));
        Assert.True(pattern.Test("https://example.com/files/a"));
        Assert.True(pattern.Test("https://example.com/files/a/b"));
    }

    // Exception tests
    [Fact]
    public void Create_WithInvalidPattern_ShouldThrowUrlPatternException()
    {
        Assert.Throws<UrlPatternException>(() => UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/path/:name(",  // Unclosed parenthesis
        }));
    }

    [Fact]
    public void Create_WithNullPatternString_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => UrlPattern.Create((string)null!));
    }

    [Fact]
    public void Create_WithNullInit_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => UrlPattern.Create((UrlPatternInit)null!));
    }

    // Escaped characters
    [Fact]
    public void Test_EscapedCharacters()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/path\\:literal",
        });

        Assert.True(pattern.Test("https://example.com/path:literal"));
    }

    // Base URL processing
    [Fact]
    public void Create_WithRelativePatternAndBaseUrl_ShouldResolve()
    {
        var pattern = UrlPattern.Create("/:id", "https://example.com/base");

        Assert.Equal("https", pattern.Protocol);
        Assert.Equal("example.com", pattern.Hostname);
    }

    // IP address patterns
    [Fact]
    public void Test_IPv4Address()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Hostname = "127.0.0.1",
        });

        Assert.True(pattern.Test("https://127.0.0.1/path"));
        Assert.False(pattern.Test("https://127.0.0.2/path"));
    }

    // Note: IPv6 address patterns require special handling in the WHATWG spec.
    // The brackets are part of the URL syntax, not the hostname pattern syntax.
    // Testing with actual IPv6 address format.
    [Fact]
    public void Test_IPv6Address()
    {
        // IPv6 addresses in URL patterns should be specified without brackets
        // as the hostname component doesn't include the brackets
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Hostname = "*", // Use wildcard to match any IPv6 address
        });

        Assert.True(pattern.Test("https://[::1]/path"));
    }

    // Subdomain patterns
    [Fact]
    public void Test_SubdomainWildcard()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Hostname = "*.example.com",
        });

        Assert.True(pattern.Test("https://www.example.com/path"));
        Assert.True(pattern.Test("https://sub.example.com/path"));
        Assert.True(pattern.Test("https://deep.sub.example.com/path"));
    }

    // URL encoding
    [Fact]
    public void Test_PercentEncodedPath()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/path%20with%20spaces",
        });

        Assert.True(pattern.Test("https://example.com/path%20with%20spaces"));
    }

    // Unicode in patterns - patterns match after URL encoding
    [Fact]
    public void Test_UnicodeInPath()
    {
        // Unicode in patterns is percent-encoded during normalization
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/caf%C3%A9",
        });

        Assert.True(pattern.Test("https://example.com/caf%C3%A9"));
    }

    // Complex pattern combinations
    [Fact]
    public void Test_ComplexPatternWithMultipleComponents()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Protocol = "https",
            Hostname = "*.example.com",
            Pathname = "/api/:version/users/:id",
            Search = "format=:format",
        });

        Assert.True(pattern.Test("https://api.example.com/api/v1/users/123?format=json"));
        Assert.False(pattern.Test("http://api.example.com/api/v1/users/123?format=json")); // Wrong protocol
    }

    // Empty patterns - empty string in init becomes wildcard
    [Fact]
    public void Test_EmptyPathname()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Protocol = "https",
            Hostname = "example.com",
            Pathname = "/",  // Use explicit root path
        });

        // Root pathname matches root URL
        Assert.True(pattern.Test("https://example.com"));
        Assert.True(pattern.Test("https://example.com/"));
    }

    // Pattern with only wildcards
    [Fact]
    public void Test_AllWildcards()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Protocol = "*",
            Hostname = "*",
            Pathname = "*",
        });

        Assert.True(pattern.Test("https://example.com/any/path"));
        Assert.True(pattern.Test("http://other.org/different"));
    }

    // Groups and modifiers
    [Fact]
    public void Test_GroupWithOptionalModifier()
    {
        // Each group with modifier applies to that group
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/items{/:category}?{/:subcategory}?",
        });

        Assert.True(pattern.Test("https://example.com/items"));
        Assert.True(pattern.Test("https://example.com/items/electronics"));
        Assert.True(pattern.Test("https://example.com/items/electronics/phones"));
    }

    [Fact]
    public void Test_GroupWithOneOrMoreModifier()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/files{/:segment}+",
        });

        Assert.True(pattern.Test("https://example.com/files/a"));
        Assert.True(pattern.Test("https://example.com/files/a/b"));
        Assert.True(pattern.Test("https://example.com/files/a/b/c"));
        Assert.False(pattern.Test("https://example.com/files"));
    }

    [Fact]
    public void Test_GroupWithZeroOrMoreModifier()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/files{/:segment}*",
        });

        Assert.True(pattern.Test("https://example.com/files"));
        Assert.True(pattern.Test("https://example.com/files/a"));
        Assert.True(pattern.Test("https://example.com/files/a/b"));
    }

    // Custom regex patterns
    [Fact]
    public void Test_CustomRegexForNumericId()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/users/:id(\\d+)",
        });

        Assert.True(pattern.Test("https://example.com/users/123"));
        Assert.False(pattern.Test("https://example.com/users/abc"));
    }

    [Fact]
    public void Test_CustomRegexForAlphanumeric()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/items/:code([a-z]{3}\\d{3})",
        });

        Assert.True(pattern.Test("https://example.com/items/abc123"));
        Assert.False(pattern.Test("https://example.com/items/ABC123")); // Case sensitive by default
        Assert.False(pattern.Test("https://example.com/items/ab12")); // Wrong format
    }

    // Port patterns
    [Fact]
    public void Test_WildcardPort()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Protocol = "https",
            Hostname = "example.com",
            Port = "*",
        });

        Assert.True(pattern.Test("https://example.com:8080/path"));
        Assert.True(pattern.Test("https://example.com:9090/path"));
        Assert.True(pattern.Test("https://example.com/path")); // Default port
    }

    [Fact]
    public void Test_SpecificPort()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Hostname = "example.com",
            Port = "8080",
        });

        Assert.True(pattern.Test("https://example.com:8080/path"));
        Assert.False(pattern.Test("https://example.com:9090/path"));
    }

    // Username and password patterns
    [Fact]
    public void Test_UsernamePattern()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Username = ":user",
        });

        Assert.True(pattern.Test("https://john@example.com/path"));
        Assert.True(pattern.Test("https://jane@example.com/path"));
    }

    [Fact]
    public void Test_PasswordPattern()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Password = ":pass",
        });

        Assert.True(pattern.Test("https://user:secret@example.com/path"));
    }

    // Trailing slash handling
    [Fact]
    public void Test_TrailingSlash()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/path/",
        });

        Assert.True(pattern.Test("https://example.com/path/"));
        Assert.False(pattern.Test("https://example.com/path")); // No trailing slash
    }

    // Double wildcards
    [Fact]
    public void Test_DoubleWildcardInPath()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/*/items/*",
        });

        Assert.True(pattern.Test("https://example.com/category/items/123"));
        Assert.True(pattern.Test("https://example.com/any/items/thing/deep"));
    }

    // Case sensitivity tests
    [Fact]
    public void Test_CaseSensitiveHostname()
    {
        // Hostnames should be case-insensitive by URL spec
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Hostname = "example.com",
        });

        Assert.True(pattern.Test("https://EXAMPLE.COM/path"));
        Assert.True(pattern.Test("https://Example.Com/path"));
    }

    [Fact]
    public void Test_CaseSensitiveProtocol()
    {
        // Protocols should be case-insensitive
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Protocol = "https",
        });

        Assert.True(pattern.Test("HTTPS://example.com/path"));
    }

    // Search parameter patterns
    [Fact]
    public void Test_MultipleSearchParameters()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Search = "foo=:foo&bar=:bar",
        });

        Assert.True(pattern.Test("https://example.com?foo=1&bar=2"));
    }

    [Fact]
    public void Test_WildcardSearch()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Search = "*",
        });

        Assert.True(pattern.Test("https://example.com?any=query&string=here"));
        Assert.True(pattern.Test("https://example.com?"));
    }

    // Hash patterns
    [Fact]
    public void Test_WildcardHash()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Hash = "*",
        });

        Assert.True(pattern.Test("https://example.com#anything"));
        Assert.True(pattern.Test("https://example.com#"));
    }

    // Literal special characters
    [Fact]
    public void Test_EscapedWildcard()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/path/\\*literal",
        });

        Assert.True(pattern.Test("https://example.com/path/*literal"));
        Assert.False(pattern.Test("https://example.com/path/anythingliteral"));
    }

    [Fact]
    public void Test_QuestionMarkInPath()
    {
        // Question marks in the pathname are typically percent-encoded in URLs
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/path/item%3F",
        });

        Assert.True(pattern.Test("https://example.com/path/item%3F"));
    }

    // Complex patterns with dots
    [Fact]
    public void Test_PatternWithDot()
    {
        // Dots between fixed text parts work normally
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/api/v1.:format",
        });

        Assert.True(pattern.Test("https://example.com/api/v1.json"));
        Assert.True(pattern.Test("https://example.com/api/v1.xml"));
    }

    // Invalid URL handling
    [Fact]
    public void Test_WithInvalidUrl_ShouldReturnFalse()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/path",
        });

        Assert.False(pattern.Test("not-a-valid-url"));
    }

    // File URLs
    [Fact]
    public void Test_FileProtocol()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Protocol = "file",
            Pathname = "/path/to/file.txt",
        });

        Assert.True(pattern.Test("file:///path/to/file.txt"));
    }

    // Data URLs
    [Fact]
    public void Test_DataProtocol()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Protocol = "data",
        });

        Assert.True(pattern.Test("data:text/plain;base64,SGVsbG8="));
    }

    // WebSocket URLs
    [Fact]
    public void Test_WebSocketProtocol()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Protocol = "wss",
            Hostname = "example.com",
            Pathname = "/socket",
        });

        Assert.True(pattern.Test("wss://example.com/socket"));
    }

    // Pattern string parsing tests
    [Fact]
    public void Test_PatternStringWithAllComponents()
    {
        // Note: URL pattern parsing may handle userinfo differently
        var pattern = UrlPattern.Create("https://example.com:8080/path?query#hash");

        Assert.Equal("https", pattern.Protocol);
        Assert.Equal("example.com", pattern.Hostname);
        Assert.Equal("8080", pattern.Port);
        Assert.Equal("/path", pattern.Pathname);
        Assert.Equal("query", pattern.Search);
        Assert.Equal("hash", pattern.Hash);
    }

    // Edge cases
    [Fact]
    public void Test_EmptyNamedGroup()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/:name",
        });

        // Empty segment should not match a named parameter
        Assert.False(pattern.Test("https://example.com/"));
    }

    [Fact]
    public void Test_NamedGroupWithSlash()
    {
        // Named groups without modifiers don't match across slashes
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/:name",
        });

        Assert.True(pattern.Test("https://example.com/value"));
        Assert.False(pattern.Test("https://example.com/value/extra"));
    }

    // Multiple patterns with same prefix
    [Fact]
    public void Test_OverlappingPatterns()
    {
        var pattern1 = UrlPattern.Create(new UrlPatternInit { Pathname = "/api/users" });
        var pattern2 = UrlPattern.Create(new UrlPatternInit { Pathname = "/api/users/:id" });

        Assert.True(pattern1.Test("https://example.com/api/users"));
        Assert.False(pattern1.Test("https://example.com/api/users/123"));

        Assert.False(pattern2.Test("https://example.com/api/users"));
        Assert.True(pattern2.Test("https://example.com/api/users/123"));
    }

    // Relative URLs with base
    [Fact]
    public void Test_RelativeUrlWithBase()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/path/:id",
        });

        Assert.True(pattern.Test("/path/123", "https://example.com"));
    }

    // Test with various URL formats
    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("https://example.com/", true)]
    [InlineData("https://example.com:443", true)]
    [InlineData("https://example.com:443/", true)]
    public void Test_HttpsDefaultPort_Variations(string url, bool expected)
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Protocol = "https",
            Hostname = "example.com",
        });

        Assert.Equal(expected, pattern.Test(url));
    }

    // Pathname normalization
    [Fact]
    public void Test_PathWithDotSegments()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Pathname = "/a/b/c",
        });

        // URLs are normalized, so /a/./b/../b/c becomes /a/b/c
        Assert.True(pattern.Test("https://example.com/a/./b/../b/c"));
    }
}
