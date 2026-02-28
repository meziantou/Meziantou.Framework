namespace Meziantou.Framework;

/// <summary>
/// Provides helper methods to hash and verify passwords using BCrypt.
/// </summary>
/// <remarks>
/// <para>
/// BCrypt hashes are encoded as modular crypt strings such as <c>$2b$12$...</c> where the revision is
/// one of <c>2</c>, <c>2a</c>, <c>2b</c>, <c>2x</c>, or <c>2y</c>, and the work factor (cost) is in the range
/// <c>4</c> to <c>31</c>.
/// </para>
/// <para>
/// Per the BCrypt specification, input is processed as UTF-8 and only the first 72 bytes are used.
/// </para>
/// </remarks>
public static partial class Bcrypt
{
    /// <summary>Minimum BCrypt work factor (cost).</summary>
    public const int MinWorkFactor = 4;

    /// <summary>Maximum BCrypt work factor (cost).</summary>
    public const int MaxWorkFactor = 31;

    /// <summary>Default BCrypt work factor.</summary>
    public const int DefaultWorkFactor = 11;

    /// <summary>Maximum number of UTF-8 bytes used by BCrypt for a password.</summary>
    public const int MaxPasswordLengthInBytes = 72;

    /// <summary>Generates a BCrypt salt for the specified work factor and revision.</summary>
    /// <param name="workFactor">The BCrypt work factor. Must be between <see cref="MinWorkFactor"/> and <see cref="MaxWorkFactor"/>.</param>
    /// <param name="version">The BCrypt revision to generate.</param>
    /// <returns>A BCrypt salt string.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="workFactor"/> is outside the supported range.</exception>
    /// <exception cref="NotSupportedException"><paramref name="version"/> is <see cref="BcryptVersion.Revision2"/>, which cannot be generated.</exception>
    public static string GenerateSalt(int workFactor = DefaultWorkFactor, BcryptVersion version = BcryptVersion.Revision2B)
    {
        var minorRevision = ToMinorRevision(version);
        return BcryptImplementation.GenerateSalt(workFactor, minorRevision);
    }

    /// <summary>Hashes a password using BCrypt and a generated salt.</summary>
    /// <param name="password">The password to hash.</param>
    /// <param name="workFactor">The BCrypt work factor. Must be between <see cref="MinWorkFactor"/> and <see cref="MaxWorkFactor"/>.</param>
    /// <param name="version">The BCrypt revision to generate.</param>
    /// <returns>The BCrypt hash string.</returns>
    public static string HashPassword(string password, int workFactor = DefaultWorkFactor, BcryptVersion version = BcryptVersion.Revision2B)
    {
        ArgumentNullException.ThrowIfNull(password);
        return HashPassword(password.AsSpan(), workFactor, version);
    }

    /// <summary>Hashes a password using BCrypt and a generated salt.</summary>
    /// <param name="password">The password to hash.</param>
    /// <param name="workFactor">The BCrypt work factor. Must be between <see cref="MinWorkFactor"/> and <see cref="MaxWorkFactor"/>.</param>
    /// <param name="version">The BCrypt revision to generate.</param>
    /// <returns>The BCrypt hash string.</returns>
    public static string HashPassword(ReadOnlySpan<char> password, int workFactor = DefaultWorkFactor, BcryptVersion version = BcryptVersion.Revision2B)
    {
        return HashPassword(password, GenerateSalt(workFactor, version));
    }

    /// <summary>Hashes a password using a specific BCrypt salt.</summary>
    /// <param name="password">The password to hash.</param>
    /// <param name="salt">The BCrypt salt string.</param>
    /// <returns>The BCrypt hash string.</returns>
    public static string HashPassword(string password, string salt)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(salt);

        return HashPassword(password.AsSpan(), salt.AsSpan());
    }

    /// <summary>Hashes a password using a specific BCrypt salt.</summary>
    /// <param name="password">The password to hash.</param>
    /// <param name="salt">The BCrypt salt string.</param>
    /// <returns>The BCrypt hash string.</returns>
    public static string HashPassword(ReadOnlySpan<char> password, ReadOnlySpan<char> salt)
    {
        return BcryptImplementation.HashPassword(password, salt);
    }

    /// <summary>Verifies that a password matches a BCrypt hash.</summary>
    /// <param name="password">The plaintext password.</param>
    /// <param name="hash">The BCrypt hash string.</param>
    /// <returns><see langword="true"/> if the password matches; otherwise, <see langword="false"/>.</returns>
    public static bool Verify(string password, string hash)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(hash);

        return Verify(password.AsSpan(), hash.AsSpan());
    }

    /// <summary>Verifies that a password matches a BCrypt hash.</summary>
    /// <param name="password">The plaintext password.</param>
    /// <param name="hash">The BCrypt hash string.</param>
    /// <returns><see langword="true"/> if the password matches; otherwise, <see langword="false"/>.</returns>
    public static bool Verify(ReadOnlySpan<char> password, ReadOnlySpan<char> hash)
    {
        if (!TryParseHash(hash, out _))
            return false;

        return BcryptImplementation.Verify(password, hash);
    }

    /// <summary>Parses a BCrypt hash and returns its revision and work factor.</summary>
    /// <param name="hash">The BCrypt hash string.</param>
    /// <returns>The parsed hash information.</returns>
    /// <exception cref="FormatException">The hash is not a valid BCrypt hash format.</exception>
    public static BcryptHashInfo ParseHash(string hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        return ParseHash(hash.AsSpan());
    }

    /// <summary>Parses a BCrypt hash and returns its revision and work factor.</summary>
    /// <param name="hash">The BCrypt hash string.</param>
    /// <returns>The parsed hash information.</returns>
    /// <exception cref="FormatException">The hash is not a valid BCrypt hash format.</exception>
    public static BcryptHashInfo ParseHash(ReadOnlySpan<char> hash)
    {
        if (TryParseHash(hash, out var result))
            return result;

        throw new FormatException("The provided hash is not a valid BCrypt hash format.");
    }

    /// <summary>Attempts to parse a BCrypt hash and extract its revision and work factor.</summary>
    /// <param name="hash">The BCrypt hash string.</param>
    /// <param name="result">When this method returns, contains parsed hash information if parsing succeeded.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryParseHash(string? hash, out BcryptHashInfo result)
    {
        result = default;
        if (hash is null)
            return false;

        return TryParseHash(hash.AsSpan(), out result);
    }

    /// <summary>Attempts to parse a BCrypt hash and extract its revision and work factor.</summary>
    /// <param name="hash">The BCrypt hash string.</param>
    /// <param name="result">When this method returns, contains parsed hash information if parsing succeeded.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryParseHash(ReadOnlySpan<char> hash, out BcryptHashInfo result)
    {
        result = default;
        if (hash.Length < 59 || hash[0] != '$' || hash[1] != '2')
            return false;

        var index = 2;
        BcryptVersion version;
        if (hash[index] == '$')
        {
            version = BcryptVersion.Revision2;
            index++;
        }
        else
        {
            if (hash.Length < 60)
                return false;

            version = hash[index] switch
            {
                'a' => BcryptVersion.Revision2A,
                'b' => BcryptVersion.Revision2B,
                'x' => BcryptVersion.Revision2X,
                'y' => BcryptVersion.Revision2Y,
                _ => default,
            };

            if (version == default || hash[index + 1] != '$')
                return false;

            index += 2;
        }

        if (hash.Length != index + 56)
            return false;

        var tensDigit = hash[index] - '0';
        var unitsDigit = hash[index + 1] - '0';
        if (tensDigit is < 0 or > 9 || unitsDigit is < 0 or > 9)
            return false;

        var workFactor = (tensDigit * 10) + unitsDigit;
        if (workFactor is < MinWorkFactor or > MaxWorkFactor)
            return false;

        if (hash[index + 2] != '$')
            return false;

        var payload = hash[(index + 3)..];
        foreach (var c in payload)
        {
            if (!IsValidBcryptHashChar(c))
                return false;
        }

        result = new BcryptHashInfo(version, workFactor);
        return true;
    }

    private static bool IsValidBcryptHashChar(char c)
    {
        return c is '.' or '/'
            || (c >= 'A' && c <= 'Z')
            || (c >= 'a' && c <= 'z')
            || (c >= '0' && c <= '9');
    }

    /// <summary>Determines whether an existing hash should be rehashed using new settings.</summary>
    /// <param name="hash">The existing BCrypt hash.</param>
    /// <param name="workFactor">The desired work factor.</param>
    /// <param name="version">The desired BCrypt revision.</param>
    /// <returns><see langword="true"/> if the hash should be replaced; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="FormatException">The hash is not a valid BCrypt hash format.</exception>
    public static bool NeedsRehash(string hash, int workFactor = DefaultWorkFactor, BcryptVersion version = BcryptVersion.Revision2B)
    {
        ArgumentNullException.ThrowIfNull(hash);
        return NeedsRehash(hash.AsSpan(), workFactor, version);
    }

    /// <summary>Determines whether an existing hash should be rehashed using new settings.</summary>
    /// <param name="hash">The existing BCrypt hash.</param>
    /// <param name="workFactor">The desired work factor.</param>
    /// <param name="version">The desired BCrypt revision.</param>
    /// <returns><see langword="true"/> if the hash should be replaced; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="FormatException">The hash is not a valid BCrypt hash format.</exception>
    public static bool NeedsRehash(ReadOnlySpan<char> hash, int workFactor = DefaultWorkFactor, BcryptVersion version = BcryptVersion.Revision2B)
    {
        if (workFactor is < MinWorkFactor or > MaxWorkFactor)
            throw new ArgumentOutOfRangeException(nameof(workFactor), workFactor, $"The work factor must be between {MinWorkFactor} and {MaxWorkFactor} (inclusive)");

        var hashInfo = ParseHash(hash);
        return hashInfo.WorkFactor != workFactor || hashInfo.Version != version;
    }

    private static char ToMinorRevision(BcryptVersion version)
    {
        return version switch
        {
            BcryptVersion.Revision2A => 'a',
            BcryptVersion.Revision2B => 'b',
            BcryptVersion.Revision2X => 'x',
            BcryptVersion.Revision2Y => 'y',
            BcryptVersion.Revision2 => throw new NotSupportedException("Generating salts for the legacy '$2$' revision is not supported."),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported BCrypt version"),
        };
    }
}