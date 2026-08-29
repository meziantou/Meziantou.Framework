namespace Meziantou.Framework.Http.Tests;

public sealed class HtpasswdFileTests
{
    [Fact]
    public void Parse_String_ShouldPopulateEntries()
    {
        const string Content = """
            # Comment
            alice:password
            bob:secret
            invalid
            """;

        var htpasswd = HtpasswdFile.Parse(Content);

        Assert.Equal(2, htpasswd.Count);
        Assert.Equal(["alice", "bob"], htpasswd.Usernames.OrderBy(value => value, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("alice:")]
    [InlineData("alice:   ")]
    [InlineData("alice:\t")]
    public void Parse_ShouldSkipEntryWithEmptyPasswordHash(string content)
    {
        var htpasswd = HtpasswdFile.Parse(content);

        Assert.Equal(0, htpasswd.Count);
        Assert.False(htpasswd.VerifyCredentials("alice", ""));
    }

    [Fact]
    public void Parse_ShouldSkipEntryWithEmptyPasswordHash_AndKeepValidEntries()
    {
        var htpasswd = HtpasswdFile.Parse("alice:\nbob:{SHA}5en6G6MezRroT3XKqkdPOmY/BfQ=");

        Assert.Equal(["bob"], htpasswd.Usernames);
        Assert.False(htpasswd.VerifyCredentials("alice", ""));
        Assert.True(htpasswd.VerifyCredentials("bob", "secret"));
    }

    [Fact]
    public void Parse_Span_ShouldPopulateEntries()
    {
        const string Content = "alice:password";

        var htpasswd = HtpasswdFile.Parse(Content.AsSpan());

        Assert.Equal(1, htpasswd.Count);
        Assert.Equal(["alice"], htpasswd.Usernames);
    }

    [Fact]
    public async Task LoadAsync_String_ShouldLoadFile()
    {
        var filePath = Path.GetTempFileName();

        try
        {
            File.WriteAllText(filePath, "alice:password");

            var htpasswd = await HtpasswdFile.LoadAsync(filePath);

            Assert.True(htpasswd.VerifyCredentials("alice", "password"));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task LoadAsync_TextReader_ShouldLoadFile()
    {
        using var reader = new StringReader("alice:password");

        var htpasswd = await HtpasswdFile.LoadAsync(reader);

        Assert.True(htpasswd.VerifyCredentials("alice", "password"));
    }

    [Fact]
    public async Task LoadAsync_TextReader_ShouldObserveTheCancellationToken()
    {
        using var reader = new StringReader("alice:password");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HtpasswdFile.LoadAsync(reader, cts.Token));
    }

    [Fact]
    public async Task LoadAsync_String_ShouldObserveTheCancellationToken()
    {
        var filePath = Path.GetTempFileName();

        try
        {
            File.WriteAllText(filePath, "alice:password");
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HtpasswdFile.LoadAsync(filePath, cts.Token));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void VerifyCredentials_String_ShouldValidateBcryptHash()
    {
        var hash = Bcrypt.HashPassword("password", workFactor: Bcrypt.MinWorkFactor, version: BcryptVersion.Revision2Y);
        var htpasswd = HtpasswdFile.Parse($"alice:{hash}");

        Assert.True(htpasswd.VerifyCredentials("alice", "password"));
        Assert.False(htpasswd.VerifyCredentials("alice", "invalid"));
    }

    [Theory]
    [InlineData(BcryptVersion.Revision2A)]
    [InlineData(BcryptVersion.Revision2B)]
    [InlineData(BcryptVersion.Revision2Y)]
    public void VerifyCredentials_ShouldValidateTheDocumentedBcryptRevisions(BcryptVersion version)
    {
        var hash = Bcrypt.HashPassword("password", workFactor: Bcrypt.MinWorkFactor, version: version);
        var htpasswd = HtpasswdFile.Parse($"alice:{hash}");

        Assert.True(htpasswd.VerifyCredentials("alice", "password"));
    }

    [Theory]
    [InlineData("$2$")]
    [InlineData("$2x$")]
    public void VerifyCredentials_ShouldNotRouteUndocumentedBcryptRevisionsToBcrypt(string prefix)
    {
        var hash = Bcrypt.HashPassword("password", workFactor: Bcrypt.MinWorkFactor, version: BcryptVersion.Revision2Y);
        var htpasswd = HtpasswdFile.Parse($"alice:{prefix}{hash[4..]}");

        Assert.False(htpasswd.VerifyCredentials("alice", "password"));
    }

    [Fact]
    public void VerifyCredentials_String_ShouldValidateSha1Hash()
    {
        var htpasswd = HtpasswdFile.Parse("alice:{SHA}W6ph5Mm5Pz8GgiULbPgzG37mj9g=");

        Assert.True(htpasswd.VerifyCredentials("alice", "password"));
        Assert.False(htpasswd.VerifyCredentials("alice", "invalid"));
    }

    [Fact]
    public void VerifyCredentials_String_ShouldValidateApr1Hash()
    {
        var htpasswd = HtpasswdFile.Parse("alice:$apr1$salt1234$k3J5yKYW6TlGmTytnkXbQ0");

        Assert.True(htpasswd.VerifyCredentials("alice", "password"));
        Assert.False(htpasswd.VerifyCredentials("alice", "invalid"));
    }

    [Fact]
    public void VerifyCredentials_String_ShouldValidateMd5CryptHash()
    {
        var htpasswd = HtpasswdFile.Parse("alice:$1$salt1234$HJCsv4hSeVLHo3hVyl4nh0");

        Assert.True(htpasswd.VerifyCredentials("alice", "password"));
        Assert.False(htpasswd.VerifyCredentials("alice", "invalid"));
    }

    [Theory]
    [InlineData("alice:$1$salt1234$HJCsv4hSeVLHo3hVyl4nh1")]
    [InlineData("alice:$1$salt1235$HJCsv4hSeVLHo3hVyl4nh0")]
    [InlineData("alice:$1$salt1234$")]
    [InlineData("alice:$1$salt1234")]
    [InlineData("alice:$1$toolongsalt$HJCsv4hSeVLHo3hVyl4nh0")]
    [InlineData("alice:$1$salt1234$HJCsv4hSeVLHo3hVyl4n!0")]
    public void VerifyCredentials_ShouldRejectAMalformedOrWrongMd5CryptHash(string entry)
    {
        var htpasswd = HtpasswdFile.Parse(entry);

        Assert.False(htpasswd.VerifyCredentials("alice", "password"));
    }

    [Fact]
    public void VerifyCredentials_String_ShouldValidateSha256CryptHash()
    {
        var htpasswd = HtpasswdFile.Parse("alice:$5$rounds=5000$toolongsaltstrin$Un/5jzAHMgOGZ5.mWJpuVolil07guHPvOW8mGRcvxa5");

        Assert.True(htpasswd.VerifyCredentials("alice", "This is just a test"));
        Assert.False(htpasswd.VerifyCredentials("alice", "invalid"));
    }

    [Theory]
    [InlineData("$5$rounds=999$toolongsaltstrin$Un/5jzAHMgOGZ5.mWJpuVolil07guHPvOW8mGRcvxa5")]
    [InlineData("$5$rounds=10000001$toolongsaltstrin$Un/5jzAHMgOGZ5.mWJpuVolil07guHPvOW8mGRcvxa5")]
    [InlineData("$5$rounds=999999999$toolongsaltstrin$Un/5jzAHMgOGZ5.mWJpuVolil07guHPvOW8mGRcvxa5")]
    public void VerifyCredentials_ShouldRejectShaCryptRoundsOutsideTheSupportedRange(string hash)
    {
        var htpasswd = HtpasswdFile.Parse($"alice:{hash}");

        Assert.False(htpasswd.VerifyCredentials("alice", "This is just a test"));
    }

    [Fact]
    public void VerifyCredentials_String_ShouldValidateSha512CryptHash()
    {
        var htpasswd = HtpasswdFile.Parse("alice:$6$rounds=5000$toolongsaltstrin$lQ8jolhgVRVhY4b5pZKaysCLi0QBxGoNeKQzQ3glMhwllF7oGDZxUhx1yxdYcz/e1JSbq3y6JMxxl8audkUEm0");

        Assert.True(htpasswd.VerifyCredentials("alice", "This is just a test"));
        Assert.False(htpasswd.VerifyCredentials("alice", "invalid"));
    }

    [Fact]
    public void VerifyCredentials_Span_ShouldValidatePlaintextPassword()
    {
        var htpasswd = HtpasswdFile.Parse("alice:password");

        Assert.True(htpasswd.VerifyCredentials("alice".AsSpan(), "password".AsSpan()));
        Assert.False(htpasswd.VerifyCredentials("alice".AsSpan(), "invalid".AsSpan()));
        Assert.False(htpasswd.VerifyCredentials("unknown".AsSpan(), "password".AsSpan()));
    }
}