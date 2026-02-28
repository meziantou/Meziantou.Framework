using System.Text;
using Xunit;

namespace Meziantou.Framework.Tests;

public sealed class BcryptTests
{
    [Theory]
    [InlineData("", "$2a$06$DCq7YPn5Rq63x1Lad4cll.", "$2a$06$DCq7YPn5Rq63x1Lad4cll.TV4S6ytwfsfvkgY8jIucDrjc8deX1s.")]
    [InlineData("abc", "$2a$10$WvvTPHKwdBJ3uk0Z37EMR.", "$2a$10$WvvTPHKwdBJ3uk0Z37EMR.hLA2W6N9AEBhEgrAOljy2Ae5MtaSIUi")]
    [InlineData("abcdefghijklmnopqrstuvwxyz", "$2a$08$aTsUwsyowQuzRrDqFflhge", "$2a$08$aTsUwsyowQuzRrDqFflhgekJ8d9/7Z3GV3UcgvzQW3J5zMyrTvlz.")]
    [InlineData("Kk4DQuMMfZL9o", "$2b$04$cVWp4XaNU8a4v1uMRum2SO", "$2b$04$cVWp4XaNU8a4v1uMRum2SO026BWLIoQMD/TXg5uZV.0P.uO8m3YEm")]
    [InlineData("9IeRXmnGxMYbs", "$2b$04$pQ7gRO7e6wx/936oXhNjrO", "$2b$04$pQ7gRO7e6wx/936oXhNjrOUNOHL1D0h1N2IDbJZYs.1ppzSof6SPy")]
    [InlineData("xVQVbwa1S0M8r", "$2b$04$SQe9knOzepOVKoYXo9xTte", "$2b$04$SQe9knOzepOVKoYXo9xTteNYr6MBwVz4tpriJVe3PNgYufGIsgKcW")]
    [InlineData("Zfgr26LWd22Za", "$2b$04$eH8zX.q5Q.j2hO1NkVYJQO", "$2b$04$eH8zX.q5Q.j2hO1NkVYJQOM6KxntS/ow3.YzVmFrE4t//CoF4fvne")]
    [InlineData("Tg4daC27epFBE", "$2b$04$ahiTdwRXpUG2JLRcIznxc.", "$2b$04$ahiTdwRXpUG2JLRcIznxc.s1.ydaPGD372bsGs8NqyYjLY1inG5n2")]
    [InlineData("xhQPMmwh5ALzW", "$2b$04$nQn78dV0hGHf5wUBe0zOFu", "$2b$04$nQn78dV0hGHf5wUBe0zOFu8n07ZbWWOKoGasZKRspZxtt.vBRNMIy")]
    [InlineData("59je8h5Gj71tg", "$2b$04$cvXudZ5ugTg95W.rOjMITu", "$2b$04$cvXudZ5ugTg95W.rOjMITuM1jC0piCl3zF5cmGhzCibHZrNHkmckG")]
    [InlineData("wT4fHJa2N9WSW", "$2b$04$YYjtiq4Uh88yUsExO0RNTu", "$2b$04$YYjtiq4Uh88yUsExO0RNTuEJ.tZlsONac16A8OcLHleWFjVawfGvO")]
    [InlineData("uSgFRnQdOgm4S", "$2b$04$WLTjgY/pZSyqX/fbMbJzf.", "$2b$04$WLTjgY/pZSyqX/fbMbJzf.qxCeTMQOzgL.CimRjMHtMxd/VGKojMu")]
    [InlineData("tEPtJZXur16Vg", "$2b$04$2moPs/x/wnCfeQ5pCheMcu", "$2b$04$2moPs/x/wnCfeQ5pCheMcuSJQ/KYjOZG780UjA/SiR.KsYWNrC7SG")]
    [InlineData("vvho8C6nlVf9K", "$2b$04$HrEYC/AQ2HS77G78cQDZQ.", "$2b$04$HrEYC/AQ2HS77G78cQDZQ.r44WGcruKw03KHlnp71yVQEwpsi3xl2")]
    [InlineData("5auCCY9by0Ruf", "$2b$04$vVYgSTfB8KVbmhbZE/k3R.", "$2b$04$vVYgSTfB8KVbmhbZE/k3R.ux9A0lJUM4CZwCkHI9fifke2.rTF7MG")]
    [InlineData("U*U", "$2a$05$CCCCCCCCCCCCCCCCCCCCC.", "$2a$05$CCCCCCCCCCCCCCCCCCCCC.E5YPO9kmyuRGyh0XouQYb4YMJKvyOeW")]
    [InlineData("U*U*", "$2a$05$CCCCCCCCCCCCCCCCCCCCC.", "$2a$05$CCCCCCCCCCCCCCCCCCCCC.VGOzA784oUp/Z0DY336zx7pLYAy0lwK")]
    [InlineData("U*U*U", "$2a$05$XXXXXXXXXXXXXXXXXXXXXO", "$2a$05$XXXXXXXXXXXXXXXXXXXXXOAcXxm9kjPGEMsLznoKqmqw7tc8WCx4a")]
    [InlineData("", "$2a$05$CCCCCCCCCCCCCCCCCCCCC.", "$2a$05$CCCCCCCCCCCCCCCCCCCCC.7uG0VCzI2bS7j6ymqJi9CdcdxiRTWNy")]
    [InlineData("0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789chars after 72 are ignored", "$2a$05$abcdefghijklmnopqrstuu", "$2a$05$abcdefghijklmnopqrstuu5s2v8.iXieOjg/.AySBTTZIIVFJeBui")]
    [InlineData("allmine", "$2a$10$XajjQvNhvvRt5GSeFk1xFe", "$2a$10$XajjQvNhvvRt5GSeFk1xFeyqRrsxkhBkUiQeg0dt.wU1qD4aFDcga")]
    [InlineData("012345678901234567890123456789012345678901234567890123456", "$2a$10$XajjQvNhvvRt5GSeFk1xFe", "$2a$10$XajjQvNhvvRt5GSeFk1xFe5l47dONXg781AmZtd869sO8zfsHuw7C")]
    public void HashPassword_KnownVectors(string password, string salt, string expected)
    {
        var actual = Bcrypt.HashPassword(password, salt);
        Assert.Equal(expected, actual);

        Assert.Equal(actual, Bcrypt.HashPassword(password, salt));
        Assert.Equal(actual, Bcrypt.HashPassword(password.AsSpan(), salt.AsSpan()));

        Assert.True(Bcrypt.Verify(password, actual));
    }

    [Theory]
    [InlineData("Kk4DQuMMfZL9o", "$2b$04$cVWp4XaNU8a4v1uMRum2SO026BWLIoQMD/TXg5uZV.0P.uO8m3YEm")]
    [InlineData("U*U", "$2a$05$CCCCCCCCCCCCCCCCCCCCC.E5YPO9kmyuRGyh0XouQYb4YMJKvyOeW")]
    [InlineData("allmine", "$2a$10$XajjQvNhvvRt5GSeFk1xFeyqRrsxkhBkUiQeg0dt.wU1qD4aFDcga")]
    public void HashPassword_UsingExistingHashAsSalt_ReturnsSameHash(string password, string hash)
    {
        Assert.Equal(hash, Bcrypt.HashPassword(password, hash));
    }

    [Theory]
    [InlineData(BcryptVersion.Revision2A, "$2a$")]
    [InlineData(BcryptVersion.Revision2B, "$2b$")]
    [InlineData(BcryptVersion.Revision2X, "$2x$")]
    [InlineData(BcryptVersion.Revision2Y, "$2y$")]
    public void HashPassword_WithRevision_GeneratesExpectedPrefix(BcryptVersion version, string expectedPrefix)
    {
        var hash = Bcrypt.HashPassword("password", workFactor: 4, version);

        Assert.StartsWith(expectedPrefix + "04$", hash, StringComparison.Ordinal);
        Assert.True(Bcrypt.Verify("password", hash));
    }

    [Theory]
    [InlineData("$2$06$DCq7YPn5Rq63x1Lad4cll.TV4S6ytwfsfvkgY8jIucDrjc8deX1s.", BcryptVersion.Revision2, 6)]
    [InlineData("$2a$06$DCq7YPn5Rq63x1Lad4cll.TV4S6ytwfsfvkgY8jIucDrjc8deX1s.", BcryptVersion.Revision2A, 6)]
    [InlineData("$2b$07$uCq3i6F42wcUHItGwO84jObhWccJLbVf9vUyXMo0NEW8MkhQHuoS.", BcryptVersion.Revision2B, 7)]
    [InlineData("$2x$05$/OK.fbVrR/bpIqNJ5ianF.o./n25XVfn6oAPaUvHe.Csk4zRfsYPi", BcryptVersion.Revision2X, 5)]
    [InlineData("$2y$10$9Cb83ULoFHStLMg2iKG3p.0.ux/vJ49gZXs4FMooj44W1P8DN89Pi", BcryptVersion.Revision2Y, 10)]
    public void ParseHash_ValidHash_ReturnsInfo(string hash, BcryptVersion expectedVersion, int expectedWorkFactor)
    {
        var parsed = Bcrypt.ParseHash(hash);

        Assert.Equal(expectedVersion, parsed.Version);
        Assert.Equal(expectedWorkFactor, parsed.WorkFactor);

        Assert.True(Bcrypt.TryParseHash(hash, out var tryParsed));
        Assert.Equal(parsed, tryParsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("$2a")]
    [InlineData("$2a$10$fooo")]
    [InlineData("$3a$10$sssssssssssssssssssssshhhhhhhhhhhhhhhhhhhhhhhhhhhhhhh")]
    [InlineData("%2a$10$sssssssssssssssssssssshhhhhhhhhhhhhhhhhhhhhhhhhhhhhhh")]
    [InlineData("$2z$10$aaaaaaaaaaaaaaaaaaaaaa0000000000000000000000000000000")]
    [InlineData("$2a$3$mdEQPMOtfPX.WGZNXgF66OhmBlOGKEd66SQ7DyJPGucYYmvTJYviy")]
    [InlineData("$2a$32$aaaaaaaaaaaaaaaaaaaaaa0000000000000000000000000000000")]
    [InlineData("$2a$xx$aaaaaaaaaaaaaaaaaaaaaa0000000000000000000000000000000")]
    [InlineData("$2a$10$aaaaaaaaaaaaaaaaaaaaaa0000000000000000000000000000000extra")]
    [InlineData("$2a$10$invalid*salt*payload")]
    public void TryParseHash_InvalidHash_ReturnsFalse(string hash)
    {
        Assert.False(Bcrypt.TryParseHash(hash, out _));
        Assert.False(Bcrypt.Verify("password", hash));
    }

    [Theory]
    [InlineData("a", "$2x$12$DB3BUbYa/SsEL7kCOVji0OauTkPkB5Y1OeyfxJHM7jvMrbml5sgD2")]
    [InlineData("a", "$2y$12$DB3BUbYa/SsEL7kCOVji0OauTkPkB5Y1OeyfxJHM7jvMrbml5sgD2")]
    [InlineData("a", "$2b$12$DB3BUbYa/SsEL7kCOVji0OauTkPkB5Y1OeyfxJHM7jvMrbml5sgD2")]
    public void Verify_KnownHash_WithDifferentPrefixes(string password, string hash)
    {
        Assert.True(Bcrypt.Verify(password, hash));
    }

    [Fact]
    public void HashPassword_PasswordsLongerThan72Bytes_AreTruncated()
    {
        const string Salt = "$2b$04$xnFVhJsTzsFBTeP3PpgbMe";

        var password72 = new string('a', 72);
        var passwordWithSuffix = password72 + "extra-bytes-ignored";
        var differentInFirst72Bytes = new string('a', 71) + "b";

        var hash72 = Bcrypt.HashPassword(password72, Salt);
        var hashWithSuffix = Bcrypt.HashPassword(passwordWithSuffix, Salt);

        Assert.Equal(hash72, hashWithSuffix);
        Assert.True(Bcrypt.Verify(passwordWithSuffix, hash72));
        Assert.False(Bcrypt.Verify(differentInFirst72Bytes, hash72));
    }

    [Fact]
    public void Verify_InvalidLengthHash_ReturnsFalse()
    {
        const string Hash = "$2b$04$2Siw3Nv3Q/gTOIPetAyPr.GNj3aO0lb1E5E9UumYGKjP9BYqlNWJe";

        Assert.True(Bcrypt.Verify("dEe6XfVGrrfSH", Hash));
        Assert.False(Bcrypt.Verify("dEe6XfVGrrfSH", Hash + "extra"));
        Assert.False(Bcrypt.Verify("dEe6XfVGrrfSH", Hash[..^10]));
    }

    [Fact]
    public void NeedsRehash_ReturnsExpectedValue()
    {
        var hash = Bcrypt.HashPassword("password", workFactor: 6, version: BcryptVersion.Revision2A);

        Assert.False(Bcrypt.NeedsRehash(hash, workFactor: 6, version: BcryptVersion.Revision2A));
        Assert.True(Bcrypt.NeedsRehash(hash, workFactor: 7, version: BcryptVersion.Revision2A));
        Assert.True(Bcrypt.NeedsRehash(hash, workFactor: 6, version: BcryptVersion.Revision2B));
    }

    [Fact]
    public void SpanOverloads_Work()
    {
        var salt = Bcrypt.GenerateSalt(4, BcryptVersion.Revision2B);

        var hash = Bcrypt.HashPassword("password".AsSpan(), salt.AsSpan());

        Assert.True(Bcrypt.Verify("password".AsSpan(), hash.AsSpan()));

        var parsed = Bcrypt.ParseHash(hash.AsSpan());
        Assert.Equal(BcryptVersion.Revision2B, parsed.Version);
        Assert.Equal(4, parsed.WorkFactor);

        Assert.True(Bcrypt.TryParseHash(hash.AsSpan(), out var tryParsed));
        Assert.Equal(parsed, tryParsed);

        Assert.False(Bcrypt.NeedsRehash(hash.AsSpan(), workFactor: 4, version: BcryptVersion.Revision2B));
    }
}