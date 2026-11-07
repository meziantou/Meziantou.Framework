using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Hashing;

namespace Meziantou.Framework.Http.Htpasswd.Fuzz;

/// <summary>
/// An htpasswd file is parsed with every overload of <see cref="HtpasswdFile"/>, and the entries are compared with a
/// reference parser of the format. Verifying a password against any entry never throws, whatever its hash looks like,
/// and agrees with an oracle for the schemes that have one (bcrypt through <see cref="Bcrypt.Verify(string, string)"/>,
/// <c>{SHA}</c> through the BCL, plaintext). Files written from a username and a password always verify.
/// </summary>
public static partial class HtpasswdFuzzTargets
{
    // Verifying a bcrypt hash costs 2^cost rounds of the key schedule, and the cost comes from the hash: HtpasswdFile has
    // no cap, so "$2y$31$..." takes days to verify. The targets only verify hashes they can verify quickly.
    private const int MaxBcryptWorkFactor = 5;

    // The library rejects more than 10,000,000 rounds, which still takes seconds per verification
    private const int MaxShaCryptRounds = 10_000;
    private const int MaxShaCryptPasswordLength = 256;

    private const int MaxPasswordLength = 1024;
    private const int MaxVerifiedEntries = 8;

    private static readonly string[] BcryptPrefixes = ["$2a$", "$2b$", "$2y$"];
    private static readonly string[] CryptPrefixes = ["$apr1$", "$1$", "$5$", "$6$"];

    private static readonly string[] Tokens =
    [
        ":", "\n", "\r\n", "\r", "#", " ", "\t", "alice", "bob", "password",
        "$2a$", "$2b$", "$2y$", "$2x$", "$2$", "$04$", "$05$", "$31$", "$99$",
        "{SHA}", "=", "$apr1$", "$1$", "$5$", "$6$", "rounds=", "rounds=1000$", "rounds=5000$", "rounds=10000000$", "rounds=999999999$",
        "salt1234$", "toolongsaltstrin$", "./", "W6ph5Mm5Pz8GgiULbPgzG37mj9g=",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("htpasswd-parse", Parse) { Seeds = GetParseSeeds, Dictionary = Tokens },
        new("htpasswd-roundtrip", RoundTrip) { Seeds = GetRoundTripSeeds, Dictionary = Tokens },
    ];

    /// <summary>
    /// The first byte selects <c>allowPlaintextPasswords</c>, then comes a candidate password up to a NUL byte, then the
    /// file. Every overload parses the same entries as the reference parser, and verifying the candidate password, or the
    /// stored hash itself, against each entry never throws and matches the oracle of its scheme.
    /// </summary>
    private static void Parse(ReadOnlySpan<byte> data)
    {
        var allowPlaintextPasswords = (FuzzInput.ReadByte(ref data) & 1) is not 0;
        var candidate = FuzzText.Decode(FuzzInput.ReadUntil(ref data, 0));
        var content = FuzzText.Decode(data);

        var file = HtpasswdFile.Parse(content, allowPlaintextPasswords);
        HtpasswdFile[] others =
        [
            HtpasswdFile.Parse(content.AsSpan(), allowPlaintextPasswords),
            HtpasswdFile.LoadAsync(new StringReader(content), allowPlaintextPasswords, CancellationToken.None).GetAwaiter().GetResult(),
            .. allowPlaintextPasswords ? Array.Empty<HtpasswdFile>() : [HtpasswdFile.Parse(content), HtpasswdFile.Parse(content.AsSpan())],
        ];

        var expectedEntries = ReferenceParse(content);
        var expectedUsernames = expectedEntries.Select(entry => entry.Username).Order(StringComparer.Ordinal).ToArray();
        foreach (var parsed in (HtpasswdFile[])[file, .. others])
        {
            FuzzAssert.Equal(allowPlaintextPasswords, parsed.AllowPlaintextPasswords, "AllowPlaintextPasswords is not the parsed option");
            FuzzAssert.Equal(expectedEntries.Count, parsed.Count, "The number of entries differs from the reference parser");
            FuzzAssert.Equal(parsed.Count, parsed.Usernames.Count, "Count and Usernames disagree");
            var usernames = parsed.Usernames.Order(StringComparer.Ordinal).ToArray();
            FuzzAssert.True(usernames.SequenceEqual(expectedUsernames, StringComparer.Ordinal), $"The usernames differ from the reference parser: {string.Join(", ", usernames.Select(FuzzAssert.Format))}");
        }

        foreach (var (username, hash) in expectedEntries.Take(MaxVerifiedEntries))
        {
            CheckVerify(file, others, username, hash, candidate);
            CheckVerify(file, others, username, hash, hash);

            // A username cannot contain ':', so this one is never in the file
            FuzzAssert.False(file.VerifyCredentials(username + ":", hash), $"The unknown user {FuzzAssert.Format(username + ":")} is accepted");
        }
    }

    /// <summary>
    /// The first byte selects the hash scheme (bcrypt in each revision, <c>{SHA}</c>, plaintext), the line ending and the
    /// lines around the entry. The username and the password are read up to NUL bytes, and the rest is appended to the
    /// file. The written entry is found and the password verifies, while a different password does not.
    /// </summary>
    private static void RoundTrip(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var username = SanitizeUsername(FuzzText.Decode(FuzzInput.ReadUntil(ref data, 0)));
        var password = FuzzText.Decode(FuzzInput.ReadUntil(ref data, 0));
        var trailer = FuzzText.Decode(data);

        var scheme = (options & 0x07) % 5;
        if (scheme <= 2 && !IsValidUtf16(password))
        {
            // bcrypt cannot hash a password that UTF-8 cannot encode
            scheme = 3;
        }

        if (scheme is 4 && !CanBePlaintextHash(password))
        {
            scheme = 3;
        }

        var hash = scheme switch
        {
            0 => Bcrypt.HashPassword(password, Bcrypt.MinWorkFactor, BcryptVersion.Revision2A),
            1 => Bcrypt.HashPassword(password, Bcrypt.MinWorkFactor, BcryptVersion.Revision2B),
            2 => Bcrypt.HashPassword(password, Bcrypt.MinWorkFactor, BcryptVersion.Revision2Y),
            3 => "{SHA}" + ComputeSha1(password),
            _ => password,
        };

        var lineEnding = ((options >> 3) & 0x03) switch
        {
            0 => "\n",
            1 => "\r\n",
            _ => "\r",
        };

        var builder = new StringBuilder();
        if ((options & 0x20) is not 0)
        {
            builder.Append("# comment").Append(lineEnding).Append(lineEnding).Append("  ").Append(lineEnding);
        }

        if ((options & 0x40) is not 0)
        {
            builder.Append(" \t");
        }

        builder.Append(username).Append(':').Append(hash).Append(lineEnding).Append(trailer);
        var allowPlaintextPasswords = scheme is 4 || (options & 0x80) is not 0;
        var file = HtpasswdFile.Parse(builder.ToString(), allowPlaintextPasswords);

        FuzzAssert.True(file.Usernames.Contains(username), $"The username {FuzzAssert.Format(username)} is not found");
        FuzzAssert.Equal(password.Length <= MaxPasswordLength, file.VerifyCredentials(username, password), $"Verifying the password of {FuzzAssert.Format(username)}");
        FuzzAssert.Equal(password.Length <= MaxPasswordLength, file.VerifyCredentials(username.AsSpan(), password.AsSpan()), $"Verifying the password of {FuzzAssert.Format(username)} as spans");

        // bcrypt only uses the first 72 bytes of the password, so a suffix past them does not change the hash
        var wrongPassword = password + "x";
        if (scheme > 2 || Encoding.UTF8.GetByteCount(password) < Bcrypt.MaxPasswordLengthInBytes)
        {
            FuzzAssert.False(file.VerifyCredentials(username, wrongPassword), $"A wrong password is accepted for {FuzzAssert.Format(username)}");
        }
    }

    private static void CheckVerify(HtpasswdFile file, HtpasswdFile[] others, string username, string hash, string password)
    {
        if (!CanVerifyQuickly(hash, password))
            return;

        var result = file.VerifyCredentials(username, password);
        FuzzAssert.Equal(result, file.VerifyCredentials(username.AsSpan(), password.AsSpan()), "VerifyCredentials(string) and VerifyCredentials(ReadOnlySpan<char>) disagree");
        foreach (var other in others)
        {
            FuzzAssert.Equal(result, other.VerifyCredentials(username, password), "The overloads of Parse give files that verify differently");
        }

        var expected = GetExpectedResult(hash, password, file.AllowPlaintextPasswords);
        if (expected is not null)
        {
            FuzzAssert.Equal(expected.Value, result, $"Verifying {FuzzAssert.Format(password)} against {FuzzAssert.Format(hash)}");
        }
    }

    /// <summary>Gets the result of the verification from an oracle, or <see langword="null"/> when the scheme has none.</summary>
    private static bool? GetExpectedResult(string hash, string password, bool allowPlaintextPasswords)
    {
        if (password.Length > MaxPasswordLength)
            return false;

        // Bcrypt.Verify returns false for a malformed hash
        if (StartsWithAny(hash, BcryptPrefixes))
            return Bcrypt.Verify(password, hash);

        if (hash.StartsWith("{SHA}", StringComparison.Ordinal))
            return string.Equals(hash[5..], ComputeSha1(password), StringComparison.Ordinal);

        if (StartsWithAny(hash, CryptPrefixes))
            return null;

        return allowPlaintextPasswords && string.Equals(hash, password, StringComparison.Ordinal);
    }

    private static bool CanVerifyQuickly(string hash, string password)
    {
        if (StartsWithAny(hash, BcryptPrefixes))
        {
            // "$2y$NN$": the work factor follows the prefix. A work factor outside the range of bcrypt is rejected before
            // any round runs.
            if (hash.Length < 6 || !char.IsAsciiDigit(hash[4]) || !char.IsAsciiDigit(hash[5]))
                return true;

            var workFactor = ((hash[4] - '0') * 10) + (hash[5] - '0');
            return workFactor is <= MaxBcryptWorkFactor or > Bcrypt.MaxWorkFactor;
        }

        if (hash.StartsWith("$5$", StringComparison.Ordinal) || hash.StartsWith("$6$", StringComparison.Ordinal))
        {
            if (password.Length > MaxShaCryptPasswordLength)
                return false;

            var parameters = hash.AsSpan(3);
            if (parameters.StartsWith("rounds=", StringComparison.Ordinal))
            {
                parameters = parameters["rounds=".Length..];
                var end = parameters.IndexOf('$');
                if (end > 0 && int.TryParse(parameters[..end], NumberStyles.None, CultureInfo.InvariantCulture, out var rounds) && rounds > MaxShaCryptRounds)
                    return false;
            }
        }

        return true;
    }

    /// <summary>The format: lines end with CR, LF or CRLF, are trimmed, and are "username:hash"; the first entry of a username wins.</summary>
    private static List<(string Username, string Hash)> ReferenceParse(string content)
    {
        var entries = new List<(string Username, string Hash)>();
        var usernames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawLine in LineSeparatorRegex().Split(content))
        {
            var line = rawLine.Trim();
            if (line.Length is 0 || line[0] is '#')
                continue;

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0 || separator == line.Length - 1)
                continue;

            var username = line[..separator];
            if (usernames.Add(username))
            {
                entries.Add((username, line[(separator + 1)..]));
            }
        }

        return entries;
    }

    private static string SanitizeUsername(string username)
    {
        username = username.Replace(":", "", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal).Trim();
        if (username.Length is 0)
            return "user";

        return username[0] is '#' ? "u" + username : username;
    }

    private static bool CanBePlaintextHash(string password)
    {
        return password.Length > 0
            && password.AsSpan().IndexOfAny('\r', '\n') < 0
            && string.Equals(password, password.Trim(), StringComparison.Ordinal)
            && !StartsWithAny(password, BcryptPrefixes)
            && !StartsWithAny(password, CryptPrefixes)
            && !password.StartsWith("{SHA}", StringComparison.Ordinal);
    }

    private static bool IsValidUtf16(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool StartsWithAny(string value, string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string ComputeSha1(string password)
    {
#pragma warning disable CA5350 // SHA1 is the hash of the htpasswd {SHA} format
        return Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
#pragma warning restore CA5350
    }

    private static IEnumerable<FuzzSeed> GetParseSeeds()
    {
        var index = 0;
        foreach (var content in GetTestContents().Distinct(StringComparer.Ordinal))
        {
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"test-{index}"), [0], "password\0" + content);
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"test-{index}-plaintext"), [1], "This is just a test\0" + content);
            index++;
        }

        var bcrypt = Bcrypt.HashPassword("password", "$2y$04$abcdefghijklmnopqrstue");
        yield return FuzzSeed.FromText("bcrypt", [0], "password\0alice:" + bcrypt);
        yield return FuzzSeed.FromText("bcrypt-2a", [0], "password\0alice:" + Bcrypt.HashPassword("password", "$2a$05$abcdefghijklmnopqrstuu"));
        yield return FuzzSeed.FromText("bcrypt-wrong-password", [0], "invalid\0alice:" + bcrypt);
        yield return FuzzSeed.FromText("bcrypt-truncated", [0], "password\0alice:" + bcrypt[..20]);
        yield return FuzzSeed.FromText("bcrypt-no-salt", [0], "password\0alice:$2y$04$");
        yield return FuzzSeed.FromText("bcrypt-bad-work-factor", [0], "password\0alice:$2y$99$abcdefghijklmnopqrstue1234567890123456789012345678901");
        yield return FuzzSeed.FromText("all-schemes", [1], "password\0" + """
            # Comment

            alice:{SHA}W6ph5Mm5Pz8GgiULbPgzG37mj9g=
            bob:$apr1$salt1234$k3J5yKYW6TlGmTytnkXbQ0
            carol:$1$salt1234$HJCsv4hSeVLHo3hVyl4nh0
            dave:$5$rounds=5000$toolongsaltstrin$Un/5jzAHMgOGZ5.mWJpuVolil07guHPvOW8mGRcvxa5
            erin:$6$rounds=5000$toolongsaltstrin$lQ8jolhgVRVhY4b5pZKaysCLi0QBxGoNeKQzQ3glMhwllF7oGDZxUhx1yxdYcz/e1JSbq3y6JMxxl8audkUEm0
            frank:password
            """);
        yield return FuzzSeed.FromText("crlf", [0], "secret\0alice:password\r\nbob:{SHA}5en6G6MezRroT3XKqkdPOmY/BfQ=\r\n");
    }

    private static IEnumerable<FuzzSeed> GetRoundTripSeeds()
    {
        for (byte scheme = 0; scheme < 5; scheme++)
        {
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"scheme-{scheme}"), [scheme], "alice\0password\0bob:password\n");
        }

        yield return FuzzSeed.FromText("crlf-comments", [0x2B], "alice\0p@ss w0rd\0# trailing\r\nalice:other\r\n");
        yield return FuzzSeed.FromText("cr-indented", [0x50], "bob \0secret\0");
        yield return FuzzSeed.FromText("unicode", [2], "élodie\0mot de passe 🔒\0");
        yield return FuzzSeed.FromText("long-password", [1], "alice\0" + new string('a', 100) + "\0");
        yield return FuzzSeed.FromText("too-long-password", [3], "alice\0" + new string('a', MaxPasswordLength + 1) + "\0");
        yield return FuzzSeed.FromText("unpaired-surrogate", [0], "alice\0pass\uD800word\0");
    }

    /// <summary>Gets the htpasswd contents of the unit tests: the string literals, as files when they hold an entry, as the hash of an entry otherwise.</summary>
    private static IEnumerable<string> GetTestContents()
    {
        var source = FuzzResources.ReadText(typeof(HtpasswdFuzzTargets).Assembly, "HtpasswdFileTests.cs");
        foreach (Match match in RawStringRegex().Matches(source))
        {
            var indent = match.Groups["indent"].Value;
            yield return string.Join('\n', match.Groups["body"].Value.Split('\n').Select(line => line.TrimEnd('\r')).Select(line => line.StartsWith(indent, StringComparison.Ordinal) ? line[indent.Length..] : line));
        }

        foreach (Match match in StringRegex().Matches(source))
        {
            var value = Regex.Unescape(match.Groups["value"].Value);
            yield return value.Contains(':', StringComparison.Ordinal) ? value : "alice:" + value;
        }
    }

    [GeneratedRegex("\r\n|\r|\n", RegexOptions.None, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex LineSeparatorRegex();

    [GeneratedRegex("""(?<![$@"])"(?<value>(?:[^"\\\r\n]|\\.)*)"(?!")""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex StringRegex();

    [GeneratedRegex("""(?<!\$)"{3}[ \t]*\r?\n(?<body>[\s\S]*?)\r?\n(?<indent>[ \t]*)"{3}""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex RawStringRegex();
}
