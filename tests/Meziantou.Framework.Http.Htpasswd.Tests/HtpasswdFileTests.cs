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
    public void VerifyCredentials_String_ShouldValidateBcryptHash()
    {
        var hash = Bcrypt.HashPassword("password", workFactor: Bcrypt.MinWorkFactor, version: BcryptVersion.Revision2Y);
        var htpasswd = HtpasswdFile.Parse($"alice:{hash}");

        Assert.True(htpasswd.VerifyCredentials("alice", "password"));
        Assert.False(htpasswd.VerifyCredentials("alice", "invalid"));
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
    public void VerifyCredentials_String_ShouldValidateSha256CryptHash()
    {
        var htpasswd = HtpasswdFile.Parse("alice:$5$rounds=5000$toolongsaltstrin$Un/5jzAHMgOGZ5.mWJpuVolil07guHPvOW8mGRcvxa5");

        Assert.True(htpasswd.VerifyCredentials("alice", "This is just a test"));
        Assert.False(htpasswd.VerifyCredentials("alice", "invalid"));
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