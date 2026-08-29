using System.Security.Cryptography;

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

    [Fact]
    public void Parse_ShouldNotTrimThePasswordField()
    {
        var htpasswd = HtpasswdFile.Parse("alice: {SHA}W6ph5Mm5Pz8GgiULbPgzG37mj9g=");

        Assert.False(htpasswd.VerifyCredentials("alice", "password"));
    }

    [Fact]
    public void Parse_ShouldNotTrimTheUsername()
    {
        var htpasswd = HtpasswdFile.Parse("  bob :{SHA}N/omUzCtg+qoee+x4ttjgIls9jk=");

        Assert.Equal(["bob "], htpasswd.Usernames);
        Assert.True(htpasswd.VerifyCredentials("bob ", "pwd"));
        Assert.False(htpasswd.VerifyCredentials("bob", "pwd"));
    }

    [Fact]
    public void Parse_DuplicateUsername_ShouldKeepTheFirstEntry()
    {
        var htpasswd = HtpasswdFile.Parse("""
            alice:{SHA}4JlqN8E9RMOwYHSTnUP6N1m9MsE=
            bob:{SHA}5en6G6MezRroT3XKqkdPOmY/BfQ=
            alice:{SHA}NS94KaI4SwAcwSsMJhPHVkVKH2o=
            """);

        Assert.Equal(2, htpasswd.Count);
        Assert.True(htpasswd.VerifyCredentials("alice", "first"));
        Assert.False(htpasswd.VerifyCredentials("alice", "second"));
        Assert.True(htpasswd.VerifyCredentials("bob", "secret"));
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

            var htpasswd = await HtpasswdFile.LoadAsync(filePath, allowPlaintextPasswords: true);

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

        var htpasswd = await HtpasswdFile.LoadAsync(reader, allowPlaintextPasswords: true);

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
    public void VerifyCredentials_ShouldAcceptAPasswordAtTheMaximumLength()
    {
        var password = new string('a', 1024);
        var hash = Bcrypt.HashPassword(password, workFactor: Bcrypt.MinWorkFactor, version: BcryptVersion.Revision2Y);
        var htpasswd = HtpasswdFile.Parse($"alice:{hash}");

        Assert.True(htpasswd.VerifyCredentials("alice", password));
    }

    [Fact]
    public void VerifyCredentials_ShouldRejectAPasswordAboveTheMaximumLength()
    {
        var password = new string('a', 1025);
        var hash = Bcrypt.HashPassword(password, workFactor: Bcrypt.MinWorkFactor, version: BcryptVersion.Revision2Y);
        var htpasswd = HtpasswdFile.Parse($"alice:{hash}");

        Assert.False(htpasswd.VerifyCredentials("alice", password));
    }

    [Fact]
    public void VerifyCredentials_String_ShouldValidateSha1Hash()
    {
        var htpasswd = HtpasswdFile.Parse("alice:{SHA}W6ph5Mm5Pz8GgiULbPgzG37mj9g=");

        Assert.True(htpasswd.VerifyCredentials("alice", "password"));
        Assert.False(htpasswd.VerifyCredentials("alice", "invalid"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(300)]
    [InlineData(1024)]
    public void VerifyCredentials_ShouldValidateSha1Hash_WhateverThePasswordLength(int length)
    {
        var password = new string('a', length);
#pragma warning disable CA5350 // SHA1 is required to build the htpasswd {SHA} format
        var hash = Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
#pragma warning restore CA5350
        var htpasswd = HtpasswdFile.Parse($"alice:{{SHA}}{hash}");

        Assert.True(htpasswd.VerifyCredentials("alice", password));
        Assert.False(htpasswd.VerifyCredentials("alice", password + "b"));
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

    [Theory]
    [InlineData("Xassword")]
    [InlineData("passworX")]
    [InlineData("passwor")]
    [InlineData("passwordX")]
    [InlineData("")]
    public void VerifyCredentials_ShouldRejectAWrongPlaintextPassword_WhereverItDiffers(string candidate)
    {
        var htpasswd = HtpasswdFile.Parse("alice:password", allowPlaintextPasswords: true);

        Assert.False(htpasswd.VerifyCredentials("alice", candidate));
    }

    [Theory]
    [InlineData("$apr1$salt1234$k3J5yKYW6TlGmTytnkXbQ1")]
    [InlineData("$apr1$salt1234$X3J5yKYW6TlGmTytnkXbQ0")]
    public void VerifyCredentials_ShouldRejectAnApr1HashThatDiffersInASingleCharacter(string hash)
    {
        var htpasswd = HtpasswdFile.Parse($"alice:{hash}");

        Assert.False(htpasswd.VerifyCredentials("alice", "password"));
    }

    [Theory]
    [InlineData("password")]
    [InlineData("abJnggxhB/yWI")]
    public void VerifyCredentials_ShouldRejectAnUnrecognizedFormatByDefault(string candidate)
    {
        var htpasswd = HtpasswdFile.Parse("alice:abJnggxhB/yWI");

        Assert.False(htpasswd.AllowPlaintextPasswords);
        Assert.False(htpasswd.VerifyCredentials("alice", candidate));
    }

    [Fact]
    public void VerifyCredentials_ShouldStillValidateHashedEntries_WhenPlaintextIsDisabled()
    {
        var htpasswd = HtpasswdFile.Parse("alice:{SHA}W6ph5Mm5Pz8GgiULbPgzG37mj9g=");

        Assert.True(htpasswd.VerifyCredentials("alice", "password"));
    }

    [Fact]
    public void VerifyCredentials_ShouldCompareAnUnrecognizedFormatAsPlaintext_WhenEnabled()
    {
        var htpasswd = HtpasswdFile.Parse("alice:abJnggxhB/yWI", allowPlaintextPasswords: true);

        Assert.True(htpasswd.AllowPlaintextPasswords);
        Assert.True(htpasswd.VerifyCredentials("alice", "abJnggxhB/yWI"));
    }

    [Fact]
    public void VerifyCredentials_Span_ShouldLookUpAUsernameThatIsNotBackedByItsOwnString()
    {
        var htpasswd = HtpasswdFile.Parse("alice:{SHA}W6ph5Mm5Pz8GgiULbPgzG37mj9g=");
        var username = "xxaliceyy".AsSpan(2, 5);

        Assert.True(htpasswd.VerifyCredentials(username, "password"));
        Assert.False(htpasswd.VerifyCredentials(username, "invalid"));
    }

    [Fact]
    public void VerifyCredentials_Span_ShouldNotAllocateWhileLookingUpTheUsername()
    {
        var htpasswd = HtpasswdFile.Parse("alice:{SHA}W6ph5Mm5Pz8GgiULbPgzG37mj9g=");
        var username = "xxaliceyy".AsSpan(2, 5);
        for (var i = 0; i < 100; i++)
        {
            _ = htpasswd.VerifyCredentials(username, "invalid");
        }

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            _ = htpasswd.VerifyCredentials(username, "invalid");
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
    }

    [Fact]
    public void VerifyCredentials_Span_ShouldValidatePlaintextPassword()
    {
        var htpasswd = HtpasswdFile.Parse("alice:password", allowPlaintextPasswords: true);

        Assert.True(htpasswd.VerifyCredentials("alice".AsSpan(), "password".AsSpan()));
        Assert.False(htpasswd.VerifyCredentials("alice".AsSpan(), "invalid".AsSpan()));
        Assert.False(htpasswd.VerifyCredentials("unknown".AsSpan(), "password".AsSpan()));
    }
}