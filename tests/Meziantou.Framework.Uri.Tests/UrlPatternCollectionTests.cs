namespace Meziantou.Framework.Tests;

public sealed class UrlPatternCollectionTests
{
    [Fact]
    public void Add_WithPatternObject_ShouldAddToCollection()
    {
        var collection = new UrlPatternCollection();
        var pattern = UrlPattern.Create(new UrlPatternInit { Pathname = "/test" });

        collection.Add(pattern);

        Assert.Single(collection);
        Assert.Same(pattern, collection[0]);
    }

    [Fact]
    public void Add_WithPatternString_ShouldCreateAndAdd()
    {
        var collection = new UrlPatternCollection();

        var pattern = collection.Add("https://example.com/path/:id");

        Assert.Single(collection);
        Assert.Equal("/path/:id", pattern.Pathname);
    }

    [Fact]
    public void Add_WithPatternStringAndBaseUrl_ShouldCreateAndAdd()
    {
        var collection = new UrlPatternCollection();

        var pattern = collection.Add("/path/:id", "https://example.com");

        Assert.Single(collection);
        Assert.Equal("https", pattern.Protocol);
        Assert.Equal("example.com", pattern.Hostname);
    }

    [Fact]
    public void Add_WithUrlPatternInit_ShouldCreateAndAdd()
    {
        var collection = new UrlPatternCollection();

        var pattern = collection.Add(new UrlPatternInit { Pathname = "/path" });

        Assert.Single(collection);
        Assert.Equal("/path", pattern.Pathname);
    }

    [Fact]
    public void IsMatch_WithMatchingUrl_ShouldReturnTrue()
    {
        var collection = new UrlPatternCollection
        {
            UrlPattern.Create(new UrlPatternInit { Pathname = "/api/*" }),
            UrlPattern.Create(new UrlPatternInit { Pathname = "/books/:id" }),
        };

        Assert.True(collection.IsMatch("https://example.com/api/users"));
        Assert.True(collection.IsMatch("https://example.com/books/123"));
    }

    [Fact]
    public void IsMatch_WithNonMatchingUrl_ShouldReturnFalse()
    {
        var collection = new UrlPatternCollection
        {
            UrlPattern.Create(new UrlPatternInit { Pathname = "/api/*" }),
            UrlPattern.Create(new UrlPatternInit { Pathname = "/books/:id" }),
        };

        Assert.False(collection.IsMatch("https://example.com/articles/123"));
    }

    [Fact]
    public void IsMatch_WithBaseUrl_ShouldWork()
    {
        var collection = new UrlPatternCollection
        {
            UrlPattern.Create(new UrlPatternInit { Pathname = "/api/*" }),
        };

        Assert.True(collection.IsMatch("/api/test", "https://example.com"));
    }

    [Fact]
    public void IsMatch_WithUri_ShouldWork()
    {
        var collection = new UrlPatternCollection
        {
            UrlPattern.Create(new UrlPatternInit { Pathname = "/api/*" }),
        };

        var uri = new System.Uri("https://example.com/api/test");
        Assert.True(collection.IsMatch(uri));
    }

    [Fact]
    public void IsMatch_WithUrlPatternInit_ShouldWork()
    {
        var collection = new UrlPatternCollection
        {
            UrlPattern.Create(new UrlPatternInit { Protocol = "https" }),
        };

        Assert.True(collection.IsMatch(new UrlPatternInit { Protocol = "https", Hostname = "example.com" }));
    }

    [Fact]
    public void FindPattern_WithMatchingUrl_ShouldReturnFirstMatchingPattern()
    {
        var pattern1 = UrlPattern.Create(new UrlPatternInit { Pathname = "/api/*" });
        var pattern2 = UrlPattern.Create(new UrlPatternInit { Pathname = "/books/:id" });
        var collection = new UrlPatternCollection { pattern1, pattern2 };

        var result = collection.FindPattern("https://example.com/api/users");

        Assert.Same(pattern1, result);
    }

    [Fact]
    public void FindPattern_WithNonMatchingUrl_ShouldReturnNull()
    {
        var collection = new UrlPatternCollection
        {
            UrlPattern.Create(new UrlPatternInit { Pathname = "/api/*" }),
        };

        var result = collection.FindPattern("https://example.com/other/path");

        Assert.Null(result);
    }

    [Fact]
    public void FindPattern_WithBaseUrl_ShouldWork()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit { Pathname = "/api/*" });
        var collection = new UrlPatternCollection { pattern };

        var result = collection.FindPattern("/api/test", "https://example.com");

        Assert.Same(pattern, result);
    }

    [Fact]
    public void FindPattern_WithUri_ShouldWork()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit { Pathname = "/api/*" });
        var collection = new UrlPatternCollection { pattern };

        var uri = new System.Uri("https://example.com/api/test");
        var result = collection.FindPattern(uri);

        Assert.Same(pattern, result);
    }

    [Fact]
    public void Remove_ShouldRemovePattern()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit { Pathname = "/test" });
        var collection = new UrlPatternCollection { pattern };

        var removed = collection.Remove(pattern);

        Assert.True(removed);
        Assert.Empty(collection);
    }

    [Fact]
    public void Remove_WithNonExistingPattern_ShouldReturnFalse()
    {
        var pattern1 = UrlPattern.Create(new UrlPatternInit { Pathname = "/test" });
        var pattern2 = UrlPattern.Create(new UrlPatternInit { Pathname = "/other" });
        var collection = new UrlPatternCollection { pattern1 };

        var removed = collection.Remove(pattern2);

        Assert.False(removed);
        Assert.Single(collection);
    }

    [Fact]
    public void Clear_ShouldRemoveAllPatterns()
    {
        var collection = new UrlPatternCollection
        {
            UrlPattern.Create(new UrlPatternInit { Pathname = "/test1" }),
            UrlPattern.Create(new UrlPatternInit { Pathname = "/test2" }),
        };

        collection.Clear();

        Assert.Empty(collection);
    }

    [Fact]
    public void Contains_WithExistingPattern_ShouldReturnTrue()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit { Pathname = "/test" });
        var collection = new UrlPatternCollection { pattern };

        Assert.True(collection.Contains(pattern));
    }

    [Fact]
    public void Contains_WithNonExistingPattern_ShouldReturnFalse()
    {
        var pattern1 = UrlPattern.Create(new UrlPatternInit { Pathname = "/test" });
        var pattern2 = UrlPattern.Create(new UrlPatternInit { Pathname = "/other" });
        var collection = new UrlPatternCollection { pattern1 };

        Assert.False(collection.Contains(pattern2));
    }

    [Fact]
    public void Constructor_WithPatterns_ShouldInitializeCollection()
    {
        var patterns = new[]
        {
            UrlPattern.Create(new UrlPatternInit { Pathname = "/test1" }),
            UrlPattern.Create(new UrlPatternInit { Pathname = "/test2" }),
        };

        var collection = new UrlPatternCollection(patterns);

        Assert.Equal(2, collection.Count);
    }

    [Fact]
    public void GetEnumerator_ShouldEnumerateAllPatterns()
    {
        var pattern1 = UrlPattern.Create(new UrlPatternInit { Pathname = "/test1" });
        var pattern2 = UrlPattern.Create(new UrlPatternInit { Pathname = "/test2" });
        var collection = new UrlPatternCollection { pattern1, pattern2 };

        var enumerated = collection.ToList();

        Assert.Equal(2, enumerated.Count);
        Assert.Contains(pattern1, enumerated);
        Assert.Contains(pattern2, enumerated);
    }

    [Fact]
    public void Indexer_ShouldReturnPatternAtIndex()
    {
        var pattern1 = UrlPattern.Create(new UrlPatternInit { Pathname = "/test1" });
        var pattern2 = UrlPattern.Create(new UrlPatternInit { Pathname = "/test2" });
        var collection = new UrlPatternCollection { pattern1, pattern2 };

        Assert.Same(pattern1, collection[0]);
        Assert.Same(pattern2, collection[1]);
    }

    // Null argument tests
    [Fact]
    public void Add_WithNullPattern_ShouldThrowArgumentNullException()
    {
        var collection = new UrlPatternCollection();

        Assert.Throws<ArgumentNullException>(() => collection.Add((UrlPattern)null!));
    }

    [Fact]
    public void Add_WithNullPatternString_ShouldThrowArgumentNullException()
    {
        var collection = new UrlPatternCollection();

        Assert.Throws<ArgumentNullException>(() => collection.Add((string)null!));
    }

    [Fact]
    public void IsMatch_WithNullUrl_ShouldThrowArgumentNullException()
    {
        var collection = new UrlPatternCollection();

        Assert.Throws<ArgumentNullException>(() => collection.IsMatch((string)null!));
    }

    [Fact]
    public void FindPattern_WithNullUrl_ShouldThrowArgumentNullException()
    {
        var collection = new UrlPatternCollection();

        Assert.Throws<ArgumentNullException>(() => collection.FindPattern((string)null!));
    }

    [Fact]
    public void Constructor_WithNullPatterns_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new UrlPatternCollection(null!));
    }

    // Empty collection tests
    [Fact]
    public void IsMatch_OnEmptyCollection_ShouldReturnFalse()
    {
        var collection = new UrlPatternCollection();

        Assert.False(collection.IsMatch("https://example.com/test"));
    }

    [Fact]
    public void FindPattern_OnEmptyCollection_ShouldReturnNull()
    {
        var collection = new UrlPatternCollection();

        Assert.Null(collection.FindPattern("https://example.com/test"));
    }

    // Equivalent to URLPatternSetTest.Empty
    [Fact]
    public void Empty_MatchesNothing()
    {
        var collection = new UrlPatternCollection();

        Assert.False(collection.IsMatch("http://www.foo.com/bar"));
        Assert.False(collection.IsMatch("invalid"));
    }

    // Equivalent to URLPatternSetTest.One
    [Fact]
    public void One_MatchesSinglePattern()
    {
        var collection = new UrlPatternCollection
        {
            UrlPattern.Create(new UrlPatternInit
            {
                Protocol = "http",
                Hostname = "www.google.com",
                Pathname = "/*",
            }),
        };

        Assert.True(collection.IsMatch("http://www.google.com/"));
        Assert.True(collection.IsMatch("http://www.google.com/monkey"));
        Assert.False(collection.IsMatch("https://www.google.com/"));
        Assert.False(collection.IsMatch("https://www.microsoft.com/"));
    }

    // Equivalent to URLPatternSetTest.Two
    [Fact]
    public void Two_MatchesMultiplePatterns()
    {
        var collection = new UrlPatternCollection
        {
            UrlPattern.Create(new UrlPatternInit
            {
                Protocol = "http",
                Hostname = "www.google.com",
                Pathname = "/*",
            }),
            UrlPattern.Create(new UrlPatternInit
            {
                Protocol = "http",
                Hostname = "www.yahoo.com",
                Pathname = "/*",
            }),
        };

        Assert.True(collection.IsMatch("http://www.google.com/monkey"));
        Assert.True(collection.IsMatch("http://www.yahoo.com/monkey"));
        Assert.False(collection.IsMatch("https://www.apple.com/monkey"));
    }

    // Equivalent to URLPatternSetTest.Duplicates
    [Fact]
    public void Duplicates_AreAllowed()
    {
        var pattern1 = UrlPattern.Create(new UrlPatternInit
        {
            Protocol = "http",
            Hostname = "www.google.com",
            Pathname = "/*",
        });
        var pattern2 = UrlPattern.Create(new UrlPatternInit
        {
            Protocol = "http",
            Hostname = "www.google.com",
            Pathname = "/*",
        });

        var collection = new UrlPatternCollection { pattern1, pattern2 };

        // Unlike URLPatternSet which deduplicates, UrlPatternCollection allows duplicates
        Assert.Equal(2, collection.Count);
    }

    // Match priority - first match wins
    [Fact]
    public void FindPattern_ReturnsFirstMatchingPattern()
    {
        var general = UrlPattern.Create(new UrlPatternInit { Pathname = "/*" });
        var specific = UrlPattern.Create(new UrlPatternInit { Pathname = "/api/*" });

        var collection = new UrlPatternCollection { general, specific };

        // General pattern is first, so it matches
        var result = collection.FindPattern("https://example.com/api/users");
        Assert.Same(general, result);
    }

    [Fact]
    public void FindPattern_ReturnsFirstMatchingPattern_ReversedOrder()
    {
        var general = UrlPattern.Create(new UrlPatternInit { Pathname = "/*" });
        var specific = UrlPattern.Create(new UrlPatternInit { Pathname = "/api/*" });

        var collection = new UrlPatternCollection { specific, general };

        // Specific pattern is first, so it matches for /api/ paths
        var result = collection.FindPattern("https://example.com/api/users");
        Assert.Same(specific, result);
    }

    // Complex matching scenarios
    [Fact]
    public void IsMatch_MultipleProtocols()
    {
        var collection = new UrlPatternCollection
        {
            UrlPattern.Create(new UrlPatternInit { Protocol = "http" }),
            UrlPattern.Create(new UrlPatternInit { Protocol = "https" }),
        };

        Assert.True(collection.IsMatch("http://example.com"));
        Assert.True(collection.IsMatch("https://example.com"));
        Assert.False(collection.IsMatch("ftp://example.com"));
    }

    [Fact]
    public void IsMatch_MultipleHostnames()
    {
        var collection = new UrlPatternCollection
        {
            UrlPattern.Create(new UrlPatternInit { Hostname = "google.com" }),
            UrlPattern.Create(new UrlPatternInit { Hostname = "yahoo.com" }),
            UrlPattern.Create(new UrlPatternInit { Hostname = "*.reddit.com" }),
        };

        Assert.True(collection.IsMatch("https://google.com/path"));
        Assert.True(collection.IsMatch("https://yahoo.com/path"));
        Assert.True(collection.IsMatch("https://www.reddit.com/path"));
        Assert.True(collection.IsMatch("https://old.reddit.com/path"));
        Assert.False(collection.IsMatch("https://microsoft.com/path"));
    }

    // Path prefix matching
    [Fact]
    public void IsMatch_PathPrefixPatterns()
    {
        var collection = new UrlPatternCollection
        {
            UrlPattern.Create(new UrlPatternInit { Pathname = "/api/*" }),
            UrlPattern.Create(new UrlPatternInit { Pathname = "/docs/*" }),
            UrlPattern.Create(new UrlPatternInit { Pathname = "/admin/*" }),
        };

        Assert.True(collection.IsMatch("https://example.com/api/v1/users"));
        Assert.True(collection.IsMatch("https://example.com/docs/getting-started"));
        Assert.True(collection.IsMatch("https://example.com/admin/dashboard"));
        Assert.False(collection.IsMatch("https://example.com/public/index.html"));
    }

    // IP address patterns in collection
    [Fact]
    public void IsMatch_IPv4AddressPatterns()
    {
        var collection = new UrlPatternCollection
        {
            UrlPattern.Create(new UrlPatternInit { Hostname = "127.0.0.1" }),
            UrlPattern.Create(new UrlPatternInit { Hostname = "192.168.1.1" }),
        };

        Assert.True(collection.IsMatch("http://127.0.0.1/"));
        Assert.True(collection.IsMatch("http://192.168.1.1/path"));
        Assert.False(collection.IsMatch("http://10.0.0.1/"));
    }

    // Port matching in collection
    [Fact]
    public void IsMatch_PortPatterns()
    {
        var collection = new UrlPatternCollection
        {
            UrlPattern.Create(new UrlPatternInit { Port = "8080" }),
            UrlPattern.Create(new UrlPatternInit { Port = "3000" }),
        };

        Assert.True(collection.IsMatch("http://example.com:8080/"));
        Assert.True(collection.IsMatch("http://example.com:3000/"));
        Assert.False(collection.IsMatch("http://example.com:9000/"));
    }

    // Combined patterns
    [Fact]
    public void IsMatch_CombinedPatterns()
    {
        var collection = new UrlPatternCollection
        {
            UrlPattern.Create(new UrlPatternInit
            {
                Protocol = "https",
                Hostname = "api.example.com",
                Pathname = "/v1/*",
            }),
            UrlPattern.Create(new UrlPatternInit
            {
                Protocol = "https",
                Hostname = "api.example.com",
                Pathname = "/v2/*",
            }),
        };

        Assert.True(collection.IsMatch("https://api.example.com/v1/users"));
        Assert.True(collection.IsMatch("https://api.example.com/v2/users"));
        Assert.False(collection.IsMatch("https://api.example.com/v3/users"));
        Assert.False(collection.IsMatch("http://api.example.com/v1/users")); // Wrong protocol
    }

    // Test with UrlPatternInit input
    [Fact]
    public void IsMatch_WithUrlPatternInit_InCollection()
    {
        var pattern = UrlPattern.Create(new UrlPatternInit
        {
            Protocol = "https",
            Hostname = "example.com",
        });
        var collection = new UrlPatternCollection { pattern };

        var result = collection.IsMatch(new UrlPatternInit
        {
            Protocol = "https",
            Hostname = "example.com",
            Pathname = "/any/path",
        });

        Assert.True(result);
    }

    // Wildcard patterns
    [Fact]
    public void IsMatch_WildcardProtocolPatterns()
    {
        var collection = new UrlPatternCollection
        {
            UrlPattern.Create(new UrlPatternInit
            {
                Protocol = "*",
                Hostname = "secure.example.com",
            }),
        };

        Assert.True(collection.IsMatch("http://secure.example.com/"));
        Assert.True(collection.IsMatch("https://secure.example.com/"));
        Assert.True(collection.IsMatch("ftp://secure.example.com/"));
    }

    // Collection with various pattern types
    [Fact]
    public void IsMatch_MixedPatternTypes()
    {
        var collection = new UrlPatternCollection
        {
            // Fixed hostname pattern
            UrlPattern.Create(new UrlPatternInit { Hostname = "api.example.com" }),
            // Wildcard subdomain pattern
            UrlPattern.Create(new UrlPatternInit { Hostname = "*.cdn.example.com" }),
            // Path pattern
            UrlPattern.Create(new UrlPatternInit { Pathname = "/public/*" }),
        };

        Assert.True(collection.IsMatch("https://api.example.com/endpoint"));
        Assert.True(collection.IsMatch("https://img.cdn.example.com/image.png"));
        Assert.True(collection.IsMatch("https://anything.com/public/file.txt"));
    }

    // Edge case: pattern that matches everything
    [Fact]
    public void IsMatch_CatchAllPattern()
    {
        var collection = new UrlPatternCollection
        {
            UrlPattern.Create(new UrlPatternInit
            {
                Protocol = "*",
                Hostname = "*",
                Pathname = "*",
            }),
        };

        Assert.True(collection.IsMatch("http://example.com/"));
        Assert.True(collection.IsMatch("https://any.domain.org/any/path"));
        Assert.True(collection.IsMatch("ftp://files.server.net/dir/file.txt"));
    }

    // Test ordering of matches
    [Fact]
    public void MatchAll_ReturnsAllMatchingPatterns()
    {
        var general = UrlPattern.Create(new UrlPatternInit { Pathname = "/*" });
        var apiPattern = UrlPattern.Create(new UrlPatternInit { Pathname = "/api/*" });
        var usersPattern = UrlPattern.Create(new UrlPatternInit { Pathname = "/api/users/*" });

        var collection = new UrlPatternCollection { general, apiPattern, usersPattern };

        // Test that all three patterns match
        var url = "https://example.com/api/users/123";
        Assert.True(general.IsMatch(url));
        Assert.True(apiPattern.IsMatch(url));
        Assert.True(usersPattern.IsMatch(url));

        // FindPattern returns first one
        Assert.Same(general, collection.FindPattern(url));
    }

    // IReadOnlyList implementation tests
    [Fact]
    public void IReadOnlyList_Count_IsCorrect()
    {
        var collection = new UrlPatternCollection
        {
            UrlPattern.Create(new UrlPatternInit { Pathname = "/a" }),
            UrlPattern.Create(new UrlPatternInit { Pathname = "/b" }),
            UrlPattern.Create(new UrlPatternInit { Pathname = "/c" }),
        };

        Assert.Equal(3, ((IReadOnlyList<UrlPattern>)collection).Count);
    }

    [Fact]
    public void IReadOnlyList_Indexer_ReturnsCorrectPattern()
    {
        var patternA = UrlPattern.Create(new UrlPatternInit { Pathname = "/a" });
        var patternB = UrlPattern.Create(new UrlPatternInit { Pathname = "/b" });
        var collection = new UrlPatternCollection { patternA, patternB };

        // Use a method to get the interface to avoid analyzer
        AssertReadOnlyListIndexer(collection, patternA, patternB);
    }

    [SuppressMessage("Performance", "MA0149:Change type of parameter for improved performance", Justification = "Testing IReadOnlyList interface implementation")]
    private static void AssertReadOnlyListIndexer(IReadOnlyList<UrlPattern> list, UrlPattern expectedFirst, UrlPattern expectedSecond)
    {
        Assert.Same(expectedFirst, list[0]);
        Assert.Same(expectedSecond, list[1]);
    }

    // Thread safety note: UrlPatternCollection is not thread-safe
    // Users should synchronize access if needed

    // Performance test: large collection
    [Fact]
    public void LargeCollection_StillMatches()
    {
        var collection = new UrlPatternCollection();
        for (var i = 0; i < 100; i++)
        {
            collection.Add(new UrlPatternInit { Pathname = $"/path{i}/*" });
        }

        Assert.True(collection.IsMatch("https://example.com/path50/something"));
        Assert.False(collection.IsMatch("https://example.com/path200/something"));
    }

    // ===========================================
    // Exec() Method Tests
    // ===========================================

    [Fact]
    public void Match_WithMatchingUrl_ShouldReturnResult()
    {
        var collection = new UrlPatternCollection
        {
            UrlPattern.Create(new UrlPatternInit { Pathname = "/api/:version/*" }),
            UrlPattern.Create(new UrlPatternInit { Pathname = "/books/:id" }),
        };

        var result = collection.Match("https://example.com/api/v2/users");

        Assert.NotNull(result);
        Assert.Equal("v2", result.Pathname.Groups["version"]);
    }

    [Fact]
    public void Match_WithNonMatchingUrl_ShouldReturnNull()
    {
        var collection = new UrlPatternCollection
        {
            UrlPattern.Create(new UrlPatternInit { Pathname = "/api/*" }),
            UrlPattern.Create(new UrlPatternInit { Pathname = "/books/:id" }),
        };

        var result = collection.Match("https://example.com/articles/123");

        Assert.Null(result);
    }

    [Fact]
    public void Match_ShouldReturnFirstMatchingPattern()
    {
        var collection = new UrlPatternCollection
        {
            UrlPattern.Create(new UrlPatternInit { Pathname = "/data/:type" }),
            UrlPattern.Create(new UrlPatternInit { Pathname = "/:category/:id" }),
        };

        var result = collection.Match("https://example.com/data/users");

        Assert.NotNull(result);
        Assert.Equal("users", result.Pathname.Groups["type"]);
        Assert.False(result.Pathname.Groups.ContainsKey("category"));
    }

    [Fact]
    public void Match_WithBaseUrl_ShouldWork()
    {
        var collection = new UrlPatternCollection
        {
            UrlPattern.Create(new UrlPatternInit { Pathname = "/items/:id" }),
        };

        var result = collection.Match("/items/42", "https://example.com");

        Assert.NotNull(result);
        Assert.Equal("42", result.Pathname.Groups["id"]);
    }

    [Fact]
    public void Match_WithUri_ShouldWork()
    {
        var collection = new UrlPatternCollection
        {
            UrlPattern.Create(new UrlPatternInit { Pathname = "/products/:id" }),
        };

        var uri = new System.Uri("https://example.com/products/999");
        var result = collection.Match(uri);

        Assert.NotNull(result);
        Assert.Equal("999", result.Pathname.Groups["id"]);
    }

    [Fact]
    public void Match_WithUrlPatternInit_ShouldWork()
    {
        var collection = new UrlPatternCollection
        {
            UrlPattern.Create(new UrlPatternInit { Pathname = "/users/:userId" }),
        };

        var input = new UrlPatternInit
        {
            Protocol = "https",
            Hostname = "example.com",
            Pathname = "/users/123",
        };

        var result = collection.Match(input);

        Assert.NotNull(result);
        Assert.Equal("123", result.Pathname.Groups["userId"]);
    }

    [Fact]
    public void Match_WithEmptyCollection_ShouldReturnNull()
    {
        var collection = new UrlPatternCollection();

        var result = collection.Match("https://example.com/any/path");

        Assert.Null(result);
    }

    [Fact]
    public void Match_WithMultipleComponents_ShouldCaptureAll()
    {
        var collection = new UrlPatternCollection
        {
            UrlPattern.Create(new UrlPatternInit
            {
                Hostname = ":subdomain.example.com",
                Pathname = "/:resource/:id",
            }),
        };

        var result = collection.Match("https://api.example.com/users/42");

        Assert.NotNull(result);
        Assert.Equal("api", result.Hostname.Groups["subdomain"]);
        Assert.Equal("users", result.Pathname.Groups["resource"]);
        Assert.Equal("42", result.Pathname.Groups["id"]);
    }
}
