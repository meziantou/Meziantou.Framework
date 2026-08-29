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
    [InlineData(BcryptVersion.Revision2Y, "$2y$")]
    public void HashPassword_WithRevision_GeneratesExpectedPrefix(BcryptVersion version, string expectedPrefix)
    {
        var hash = Bcrypt.HashPassword("password", workFactor: 4, version);

        Assert.StartsWith(expectedPrefix + "04$", hash);
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
    [InlineData("a", "$2y$12$DB3BUbYa/SsEL7kCOVji0OauTkPkB5Y1OeyfxJHM7jvMrbml5sgD2")]
    [InlineData("a", "$2b$12$DB3BUbYa/SsEL7kCOVji0OauTkPkB5Y1OeyfxJHM7jvMrbml5sgD2")]
    public void Verify_KnownHash_WithDifferentPrefixes(string password, string hash)
    {
        Assert.True(Bcrypt.Verify(password, hash));
    }

    [Fact]
    public void Revision2X_IsRejectedEverywhereItWouldProduceAWrongResult()
    {
        const string Hash2X = "$2x$12$DB3BUbYa/SsEL7kCOVji0OauTkPkB5Y1OeyfxJHM7jvMrbml5sgD2";
        const string Salt2X = "$2x$12$DB3BUbYa/SsEL7kCOVji0Oau";

        Assert.Throws<NotSupportedException>(() => Bcrypt.GenerateSalt(12, BcryptVersion.Revision2X));
        Assert.Throws<NotSupportedException>(() => Bcrypt.HashPassword("a", workFactor: 12, BcryptVersion.Revision2X));
        Assert.Throws<NotSupportedException>(() => Bcrypt.HashPassword("a", Salt2X));
        Assert.Throws<NotSupportedException>(() => Bcrypt.Verify("a", Hash2X));
        Assert.Throws<NotSupportedException>(() => Bcrypt.Verify("a".AsSpan(), Hash2X.AsSpan()));
    }

    [Fact]
    public void Revision2X_RemainsParseableSoExistingHashesCanBeFound()
    {
        const string Hash2X = "$2x$12$DB3BUbYa/SsEL7kCOVji0OauTkPkB5Y1OeyfxJHM7jvMrbml5sgD2";

        Assert.True(Bcrypt.TryParseHash(Hash2X, out var info));
        Assert.Equal(BcryptVersion.Revision2X, info.Version);
        Assert.Equal(12, info.WorkFactor);
        Assert.True(Bcrypt.NeedsRehash(Hash2X, workFactor: 12, version: BcryptVersion.Revision2B));
    }

    [Fact]
    public void TryParseHash_NonCanonicalTrailingCharacter_ReturnsFalse()
    {
        const string Password = "abc";
        var hash = Bcrypt.HashPassword(Password, "$2b$06$DCq7YPn5Rq63x1Lad4cll.");

        Assert.True(Bcrypt.TryParseHash(hash, out _));
        Assert.True(Bcrypt.Verify(Password, hash));

        // The 22nd salt character contributes only 2 bits, so '.' and '/' decode to the same salt bytes.
        // TryParseHash used to accept the alternative spelling that Verify can never match.
        var nonCanonicalSalt = string.Concat(hash.AsSpan(0, 28), "/", hash.AsSpan(29));
        Assert.False(Bcrypt.TryParseHash(nonCanonicalSalt, out _));
        Assert.False(Bcrypt.Verify(Password, nonCanonicalSalt));

        // The trailing digest character carries 2 unused bits for the same reason.
        var nonCanonicalDigest = string.Concat(hash.AsSpan(0, hash.Length - 1), "/");
        Assert.False(Bcrypt.TryParseHash(nonCanonicalDigest, out _));
        Assert.False(Bcrypt.Verify(Password, nonCanonicalDigest));
    }

    [Theory]
    [InlineData("$2b$06$DCq7YPn5Rq63x1Lad4cll/")]
    [InlineData("$2b$06$DCq7YPn5Rq63x1Lad4c!!.")]
    [InlineData("$2b$06$DCq7YPn5Rq63x1Lad4cl*.")]
    public void HashPassword_MalformedSalt_ThrowsFormatException(string salt)
    {
        // These used to escape as ArgumentException naming private parameters ("saltBytes").
        Assert.Throws<FormatException>(() => Bcrypt.HashPassword("abc", salt));
        Assert.Throws<FormatException>(() => Bcrypt.HashPassword("abc".AsSpan(), salt.AsSpan()));
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

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("password")]
    public void HashPassword_LegacyRevision2_RoundTrips(string password)
    {
        const string Salt = "$2$06$DCq7YPn5Rq63x1Lad4cll.";

        var hash = Bcrypt.HashPassword(password, Salt);

        Assert.StartsWith("$2$06$", hash);
        Assert.HasCount(59, hash);
        Assert.True(Bcrypt.Verify(password, hash));
    }

    [Fact]
    public void Verify_EmptyPasswordAgainstLegacyRevision2Hash_DoesNotThrow()
    {
        // An empty password used to throw ArgumentNullException here: the '$2$' revision appends no NUL
        // terminator, so it is the only revision that can produce zero-length key material.
        // Zero-length key material yields the same all-zero key schedule as the single NUL byte the 'a'
        // revision appends, so this digest matches the well-known '$2a$06$' vector for an empty password.
        const string Hash = "$2$06$DCq7YPn5Rq63x1Lad4cll.TV4S6ytwfsfvkgY8jIucDrjc8deX1s.";

        Assert.True(Bcrypt.TryParseHash(Hash, out _));
        Assert.True(Bcrypt.Verify("", Hash));
        Assert.True(Bcrypt.Verify("".AsSpan(), Hash.AsSpan()));
        Assert.False(Bcrypt.Verify("not-empty", Hash));
    }

    [Fact]
    public void Verify_InvalidLengthHash_ReturnsFalse()
    {
        const string Hash = "$2b$04$2Siw3Nv3Q/gTOIPetAyPr.GNj3aO0lb1E5E9UumYGKjP9BYqlNWJe";

        Assert.True(Bcrypt.Verify("dEe6XfVGrrfSH", Hash));
        Assert.False(Bcrypt.Verify("dEe6XfVGrrfSH", Hash + "extra"));
        Assert.False(Bcrypt.Verify("dEe6XfVGrrfSH", Hash[..^10]));
    }

    [Theory]
    [InlineData("$2b$00$DCq7YPn5Rq63x1Lad4cll.")]
    [InlineData("$2b$01$DCq7YPn5Rq63x1Lad4cll.")]
    [InlineData("$2b$03$DCq7YPn5Rq63x1Lad4cll.")]
    [InlineData("$2b$32$DCq7YPn5Rq63x1Lad4cll.")]
    [InlineData("$2b$xx$DCq7YPn5Rq63x1Lad4cll.")]
    public void HashPassword_SaltWithWorkFactorOutOfRange_ThrowsFormatException(string salt)
    {
        // Work factors 1 to 3 used to pass the salt parser and then fail inside CryptRaw with
        // ArgumentException naming a private parameter ("workFactor").
        Assert.Throws<FormatException>(() => Bcrypt.HashPassword("abc", salt));
        Assert.Throws<FormatException>(() => Bcrypt.HashPassword("abc".AsSpan(), salt.AsSpan()));
    }

    [Theory]
    [InlineData(Bcrypt.MinWorkFactor)]
    [InlineData(Bcrypt.MinWorkFactor + 1)]
    public void HashPassword_SaltAtMinimumWorkFactor_Succeeds(int workFactor)
    {
        var salt = Bcrypt.GenerateSalt(workFactor);

        var hash = Bcrypt.HashPassword("abc", salt);

        Assert.True(Bcrypt.Verify("abc", hash));
        Assert.Equal(workFactor, Bcrypt.ParseHash(hash).WorkFactor);
    }

    [Fact]
    public void Verify_PasswordWithUnpairedSurrogate_ReturnsFalse()
    {
        // Unpaired surrogates used to escape as EncoderFallbackException from a method returning bool.
        const string Hash = "$2b$04$2Siw3Nv3Q/gTOIPetAyPr.GNj3aO0lb1E5E9UumYGKjP9BYqlNWJe";

        foreach (var password in UnpairedSurrogatePasswords())
        {
            Assert.False(Bcrypt.Verify(password, Hash));
            Assert.False(Bcrypt.Verify(password.AsSpan(), Hash.AsSpan()));
        }
    }

    [Fact]
    public void HashPassword_PasswordWithUnpairedSurrogate_ThrowsArgumentException()
    {
        var salt = Bcrypt.GenerateSalt(4);

        foreach (var password in UnpairedSurrogatePasswords())
        {
            // Guard against the inputs silently becoming encodable and making the assertions below vacuous.
            Assert.Throws<EncoderFallbackException>(() => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetByteCount(password));

            Assert.Equal("password", Assert.Throws<ArgumentException>(() => Bcrypt.HashPassword(password, workFactor: 4)).ParamName);
            Assert.Equal("password", Assert.Throws<ArgumentException>(() => Bcrypt.HashPassword(password, salt)).ParamName);
        }
    }

    [Fact]
    public void Verify_UnpairedSurrogateAgainstUnsupportedRevision_StillReportsTheRevision()
    {
        // The hash is validated before the password is encoded, so an unsupported revision fails loudly
        // even when the password is itself invalid: the revision is a property of stored data and the
        // caller needs to learn about it.
        const string Hash2X = "$2x$12$DB3BUbYa/SsEL7kCOVji0OauTkPkB5Y1OeyfxJHM7jvMrbml5sgD2";

        foreach (var password in UnpairedSurrogatePasswords())
        {
            Assert.Throws<NotSupportedException>(() => Bcrypt.Verify(password, Hash2X));
        }
    }

    // Built in code rather than through [InlineData]: xunit round-trips theory data through UTF-8,
    // which replaces unpaired surrogates with U+FFFD and would make these tests vacuous.
    private static string[] UnpairedSurrogatePasswords() =>
    [
        "\ud800",
        "\udc00",
        "ab\ud800cd",
        "ab\udc00cd",
        "trailing\ud83d",
    ];

    [Theory]
    [InlineData("caf\u00e9")]
    [InlineData("\u4f60\u597d\u4e16\u754c")]
    [InlineData("\ud83d\ude00 emoji")]
    [InlineData("\u00ff\u00ffabc")]
    public void HashPassword_NonAsciiPassword_RoundTrips(string password)
    {
        var hash = Bcrypt.HashPassword(password, workFactor: 4);

        Assert.True(Bcrypt.Verify(password, hash));
        Assert.False(Bcrypt.Verify(password + "x", hash));
    }

    [Fact]
    public void NeedsRehash_ReturnsExpectedValue()
    {
        var hash = Bcrypt.HashPassword("password", workFactor: 6, version: BcryptVersion.Revision2A);

        Assert.False(Bcrypt.NeedsRehash(hash, workFactor: 6, version: BcryptVersion.Revision2A));
        Assert.True(Bcrypt.NeedsRehash(hash, workFactor: 7, version: BcryptVersion.Revision2A));
        Assert.True(Bcrypt.NeedsRehash(hash, workFactor: 6, version: BcryptVersion.Revision2B));
    }

    [Theory]
    [InlineData(Bcrypt.MinWorkFactor - 1)]
    [InlineData(Bcrypt.MaxWorkFactor + 1)]
    [InlineData(0)]
    [InlineData(-1)]
    public void GenerateSalt_WorkFactorOutOfRange_Throws(int workFactor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Bcrypt.GenerateSalt(workFactor));
    }

    [Fact]
    public void GenerateSalt_UnsupportedVersion_Throws()
    {
        Assert.Throws<NotSupportedException>(() => Bcrypt.GenerateSalt(version: BcryptVersion.Revision2));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bcrypt.GenerateSalt(version: (BcryptVersion)99));
    }

    [Fact]
    public void GenerateSalt_ProducesDistinctSaltsOfExpectedShape()
    {
        var salts = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 32; i++)
        {
            var salt = Bcrypt.GenerateSalt(4);

            Assert.StartsWith("$2b$04$", salt);
            Assert.HasCount(29, salt);
            Assert.True(salts.Add(salt), "GenerateSalt returned a duplicate salt");
        }
    }

    [Fact]
    public void NeedsRehash_InvalidHash_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => Bcrypt.NeedsRehash("not-a-hash"));
        Assert.Throws<FormatException>(() => Bcrypt.NeedsRehash("not-a-hash".AsSpan()));
    }

    [Theory]
    [InlineData(Bcrypt.MinWorkFactor - 1)]
    [InlineData(Bcrypt.MaxWorkFactor + 1)]
    public void NeedsRehash_WorkFactorOutOfRange_Throws(int workFactor)
    {
        var hash = Bcrypt.HashPassword("password", workFactor: 4);

        Assert.Throws<ArgumentOutOfRangeException>(() => Bcrypt.NeedsRehash(hash, workFactor));
    }

    [Fact]
    public void NullArguments_Throw()
    {
        const string Hash = "$2b$04$2Siw3Nv3Q/gTOIPetAyPr.GNj3aO0lb1E5E9UumYGKjP9BYqlNWJe";

        Assert.Throws<ArgumentNullException>(() => Bcrypt.HashPassword(password: null!));
        Assert.Throws<ArgumentNullException>(() => Bcrypt.HashPassword(password: null!, salt: "$2b$04$2Siw3Nv3Q/gTOIPetAyPr."));
        Assert.Throws<ArgumentNullException>(() => Bcrypt.HashPassword(password: "a", salt: null!));
        Assert.Throws<ArgumentNullException>(() => Bcrypt.Verify(password: null!, hash: Hash));
        Assert.Throws<ArgumentNullException>(() => Bcrypt.Verify(password: "a", hash: null!));
        Assert.Throws<ArgumentNullException>(() => Bcrypt.ParseHash(hash: null!));
        Assert.Throws<ArgumentNullException>(() => Bcrypt.NeedsRehash(hash: null!));

        Assert.False(Bcrypt.TryParseHash(null, out _));
    }

    [Fact]
    public void BcryptHashInfo_EqualityMembers()
    {
        var info = new BcryptHashInfo(BcryptVersion.Revision2B, 11);
        var same = new BcryptHashInfo(BcryptVersion.Revision2B, 11);
        var otherVersion = new BcryptHashInfo(BcryptVersion.Revision2A, 11);
        var otherWorkFactor = new BcryptHashInfo(BcryptVersion.Revision2B, 12);

        Assert.Equal(BcryptVersion.Revision2B, info.Version);
        Assert.Equal(11, info.WorkFactor);

        Assert.Equal(same, info);
        Assert.Equal((object)same, (object)info);
        Assert.True(info == same);
        Assert.False(info != same);
        Assert.Equal(info.GetHashCode(), same.GetHashCode());

        Assert.NotEqual(otherVersion, info);
        Assert.NotEqual(otherWorkFactor, info);
        Assert.True(info != otherVersion);
        Assert.True(info != otherWorkFactor);

        object differentType = "not a BcryptHashInfo";
        Assert.False(info.Equals(differentType));
        Assert.False(info.Equals(null));
    }

    [Fact]
    public void HashPassword_MultiByteCharacterStraddlingTheLimit_IsTruncatedMidCharacter()
    {
        // Only the first 72 UTF-8 bytes are used. 'e' with an acute accent and 'e' with a circumflex
        // share their first UTF-8 byte (0xC3), which lands at byte 71 here, so the two passwords
        // are indistinguishable. This pins the behaviour rather than endorsing it.
        const string Salt = "$2b$04$xnFVhJsTzsFBTeP3PpgbMe";

        var acute = new string('a', 71) + "\u00e9zzz";
        var circumflex = new string('a', 71) + "\u00eazzz";

        Assert.Equal(Bcrypt.HashPassword(acute, Salt), Bcrypt.HashPassword(circumflex, Salt));
        Assert.True(Bcrypt.Verify(circumflex, Bcrypt.HashPassword(acute, Salt)));
    }

    [Fact]
    public void HashPassword_IsSafeToUseConcurrently()
    {
        // Bcrypt is a static facade over per-call BcryptImplementation state.
        var passwords = Enumerable.Range(0, 32).Select(i => $"password-{i}").ToArray();

        var hashes = new string[passwords.Length];
        Parallel.For(0, passwords.Length, i => hashes[i] = Bcrypt.HashPassword(passwords[i], workFactor: 4));

        for (var i = 0; i < passwords.Length; i++)
        {
            Assert.True(Bcrypt.Verify(passwords[i], hashes[i]));
        }

        Assert.HasCount(passwords.Length, hashes.Distinct(StringComparer.Ordinal).ToArray());
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