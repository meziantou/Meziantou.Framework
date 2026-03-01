using System.Buffers;
using System.Security.Cryptography;

namespace Meziantou.Framework.Http;

/// <summary>Represents an htpasswd file and provides methods to parse and verify credentials.</summary>
public sealed class HtpasswdFile
{
    private const string Apr1Prefix = "$apr1$";
    private const string Md5CryptPrefix = "$1$";
    private const string Sha256CryptPrefix = "$5$";
    private const string Sha512CryptPrefix = "$6$";
    private const string ShaCryptRoundsPrefix = "rounds=";
    private const int Md5CryptSaltMaxLength = 8;
    private const int ShaCryptSaltMaxLength = 16;
    private const int ShaCryptDefaultRounds = 5000;
    private const int ShaCryptMinRounds = 1000;
    private const int ShaCryptMaxRounds = 999_999_999;
    private const string Sha1Prefix = "{SHA}";
    private readonly Dictionary<string, string> _entries;

    private HtpasswdFile(Dictionary<string, string> entries)
    {
        _entries = entries;
    }

    /// <summary>Gets the list of usernames in the file.</summary>
    public ICollection<string> Usernames => _entries.Keys;

    /// <summary>Gets the number of entries in the file.</summary>
    public int Count => _entries.Count;

    /// <summary>Parses the content of an htpasswd file.</summary>
    /// <param name="content">The text content of the htpasswd file.</param>
    /// <returns>A parsed <see cref="HtpasswdFile"/> instance.</returns>
    public static HtpasswdFile Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Parse(content.AsSpan());
    }

    /// <summary>
    /// Parses the content of an htpasswd file.
    /// </summary>
    /// <param name="content">The text content of the htpasswd file.</param>
    /// <returns>A parsed <see cref="HtpasswdFile"/> instance.</returns>
    public static HtpasswdFile Parse(ReadOnlySpan<char> content)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);

        while (!content.IsEmpty)
        {
            var endOfLineIndex = content.IndexOfAny('\r', '\n');

            ReadOnlySpan<char> line;
            if (endOfLineIndex < 0)
            {
                line = content;
                content = [];
            }
            else
            {
                line = content[..endOfLineIndex];

                var charactersToConsume = 1;
                if (content[endOfLineIndex] == '\r' && endOfLineIndex + 1 < content.Length && content[endOfLineIndex + 1] == '\n')
                {
                    charactersToConsume++;
                }

                content = content[(endOfLineIndex + charactersToConsume)..];
            }

            line = line.Trim();
            if (line.IsEmpty || line[0] == '#')
                continue;

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
                continue;

            var username = line[..separatorIndex].Trim();
            if (username.IsEmpty)
                continue;

            var passwordHash = line[(separatorIndex + 1)..].Trim();
            entries[username.ToString()] = passwordHash.ToString();
        }

        return new HtpasswdFile(entries);
    }

    /// <summary>
    /// Loads and parses an htpasswd file from disk.
    /// </summary>
    /// <param name="file">The path of the htpasswd file.</param>
    /// <returns>A parsed <see cref="HtpasswdFile"/> instance.</returns>
    public static async Task<HtpasswdFile> LoadAsync(string file)
    {
        ArgumentNullException.ThrowIfNull(file);

        await using var stream = File.OpenRead(file);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await LoadAsync(reader).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads and parses an htpasswd file from a <see cref="TextReader"/>.
    /// </summary>
    /// <param name="file">The reader containing the htpasswd content.</param>
    /// <returns>A parsed <see cref="HtpasswdFile"/> instance.</returns>
    public static async Task<HtpasswdFile> LoadAsync(TextReader file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var content = await file.ReadToEndAsync().ConfigureAwait(false);
        return Parse(content);
    }

    /// <summary>
    /// Verifies a username and password against the current htpasswd entries.
    /// </summary>
    /// <param name="username">The username to verify.</param>
    /// <param name="password">The plaintext password to verify.</param>
    /// <returns><see langword="true"/> if the credentials are valid; otherwise, <see langword="false"/>.</returns>
    public bool VerifyCredentials(string username, string password)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);

        return VerifyCredentials(username.AsSpan(), password.AsSpan());
    }

    /// <summary>
    /// Verifies a username and password against the current htpasswd entries.
    /// </summary>
    /// <param name="username">The username to verify.</param>
    /// <param name="password">The plaintext password to verify.</param>
    /// <returns><see langword="true"/> if the credentials are valid; otherwise, <see langword="false"/>.</returns>
    public bool VerifyCredentials(ReadOnlySpan<char> username, ReadOnlySpan<char> password)
    {
        if (!_entries.TryGetValue(username.ToString(), out var expectedPasswordHash))
            return false;

        var expectedPasswordHashSpan = expectedPasswordHash.AsSpan();
        if (expectedPasswordHashSpan.StartsWith("$2", StringComparison.Ordinal))
            return Bcrypt.Verify(password, expectedPasswordHashSpan);

        if (expectedPasswordHashSpan.StartsWith(Sha1Prefix, StringComparison.Ordinal))
            return VerifySha1(password, expectedPasswordHashSpan[Sha1Prefix.Length..]);

        if (expectedPasswordHashSpan.StartsWith(Apr1Prefix, StringComparison.Ordinal))
            return VerifyMd5Crypt(password, expectedPasswordHashSpan, Apr1Prefix);

        if (expectedPasswordHashSpan.StartsWith(Md5CryptPrefix, StringComparison.Ordinal))
            return VerifyMd5Crypt(password, expectedPasswordHashSpan, Md5CryptPrefix);

        if (expectedPasswordHashSpan.StartsWith(Sha256CryptPrefix, StringComparison.Ordinal))
            return VerifyShaCrypt(password, expectedPasswordHashSpan, useSha512: false);

        if (expectedPasswordHashSpan.StartsWith(Sha512CryptPrefix, StringComparison.Ordinal))
            return VerifyShaCrypt(password, expectedPasswordHashSpan, useSha512: true);

        return password.SequenceEqual(expectedPasswordHashSpan);
    }

    private static bool VerifyMd5Crypt(ReadOnlySpan<char> password, ReadOnlySpan<char> expectedHash, string prefix)
    {
        if (!TryParseMd5CryptHash(expectedHash, prefix, out var salt, out _))
            return false;

        var computedHash = CreateMd5CryptHash(password, salt, prefix);
        return expectedHash.SequenceEqual(computedHash.AsSpan());
    }

    private static bool VerifyShaCrypt(ReadOnlySpan<char> password, ReadOnlySpan<char> expectedHash, bool useSha512)
    {
        var expectedChecksumLength = useSha512 ? 86 : 43;
        var prefix = useSha512 ? Sha512CryptPrefix : Sha256CryptPrefix;

        if (!TryParseShaCryptHash(expectedHash, prefix, expectedChecksumLength, out var rounds, out var roundsCustom, out var salt, out _))
            return false;

        var computedHash = CreateShaCryptHash(password, salt, rounds, roundsCustom, useSha512);
        return expectedHash.SequenceEqual(computedHash.AsSpan());
    }

    private static bool TryParseMd5CryptHash(ReadOnlySpan<char> hash, string prefix, out ReadOnlySpan<char> salt, out ReadOnlySpan<char> checksum)
    {
        salt = default;
        checksum = default;

        if (!hash.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var remaining = hash[prefix.Length..];
        var separatorIndex = remaining.IndexOf('$');
        if (separatorIndex < 0)
            return false;

        salt = remaining[..separatorIndex];
        checksum = remaining[(separatorIndex + 1)..];

        if (salt.Length > Md5CryptSaltMaxLength || checksum.IsEmpty || checksum.Contains('$'))
            return false;

        return IsHash64String(salt) && IsHash64String(checksum);
    }

    private static bool TryParseShaCryptHash(ReadOnlySpan<char> hash, string prefix, int expectedChecksumLength, out int rounds, out bool roundsCustom, out ReadOnlySpan<char> salt, out ReadOnlySpan<char> checksum)
    {
        rounds = ShaCryptDefaultRounds;
        roundsCustom = false;
        salt = default;
        checksum = default;

        if (!hash.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var remaining = hash[prefix.Length..];
        if (remaining.StartsWith(ShaCryptRoundsPrefix, StringComparison.Ordinal))
        {
            remaining = remaining[ShaCryptRoundsPrefix.Length..];

            var roundsSeparatorIndex = remaining.IndexOf('$');
            if (roundsSeparatorIndex <= 0)
                return false;

            var roundsText = remaining[..roundsSeparatorIndex];
            if (!int.TryParse(roundsText, NumberStyles.None, CultureInfo.InvariantCulture, out rounds))
                return false;

            rounds = Math.Clamp(rounds, ShaCryptMinRounds, ShaCryptMaxRounds);
            roundsCustom = true;
            remaining = remaining[(roundsSeparatorIndex + 1)..];
        }

        var saltSeparatorIndex = remaining.IndexOf('$');
        if (saltSeparatorIndex < 0)
            return false;

        salt = remaining[..saltSeparatorIndex];
        checksum = remaining[(saltSeparatorIndex + 1)..];

        if (salt.Length > ShaCryptSaltMaxLength || checksum.Length != expectedChecksumLength || checksum.Contains('$'))
            return false;

        return IsHash64String(salt) && IsHash64String(checksum);
    }

    private static bool IsHash64String(ReadOnlySpan<char> value)
    {
        const string Hash64Characters = "./0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        foreach (var c in value)
        {
            if (Hash64Characters.IndexOf(c, StringComparison.Ordinal) < 0)
                return false;
        }

        return true;
    }

    private static string CreateMd5CryptHash(ReadOnlySpan<char> password, ReadOnlySpan<char> salt, string prefix)
    {
#pragma warning disable CA5351 // MD5 is required to support the htpasswd $apr1$ and $1$ formats
        var passwordBytes = Encoding.UTF8.GetBytes(password.ToString());
        var saltBytes = Encoding.ASCII.GetBytes(salt.ToString());
        var prefixBytes = Encoding.ASCII.GetBytes(prefix);

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

        hasher.AppendData(passwordBytes);
        hasher.AppendData(saltBytes);
        hasher.AppendData(passwordBytes);
        var final = hasher.GetHashAndReset();

        hasher.AppendData(passwordBytes);
        hasher.AppendData(prefixBytes);
        hasher.AppendData(saltBytes);

        for (var remaining = passwordBytes.Length; remaining > 0; remaining -= final.Length)
        {
            hasher.AppendData(final.AsSpan(0, Math.Min(final.Length, remaining)));
        }

        Span<byte> zeroByte = stackalloc byte[1];
        for (var i = passwordBytes.Length; i > 0; i >>= 1)
        {
            if ((i & 1) != 0)
            {
                hasher.AppendData(zeroByte);
            }
            else if (passwordBytes.Length > 0)
            {
                hasher.AppendData(passwordBytes.AsSpan(0, 1));
            }
        }

        final = hasher.GetHashAndReset();

        for (var i = 0; i < 1000; i++)
        {
            if ((i & 1) != 0)
            {
                hasher.AppendData(passwordBytes);
            }
            else
            {
                hasher.AppendData(final);
            }

            if (i % 3 != 0)
            {
                hasher.AppendData(saltBytes);
            }

            if (i % 7 != 0)
            {
                hasher.AppendData(passwordBytes);
            }

            if ((i & 1) != 0)
            {
                hasher.AppendData(final);
            }
            else
            {
                hasher.AppendData(passwordBytes);
            }

            final = hasher.GetHashAndReset();
        }

        var output = new StringBuilder(prefix.Length + salt.Length + 23);
        output.Append(prefix);
        output.Append(salt);
        output.Append('$');
        AppendHash64From24Bit(output, final[0], final[6], final[12], 4);
        AppendHash64From24Bit(output, final[1], final[7], final[13], 4);
        AppendHash64From24Bit(output, final[2], final[8], final[14], 4);
        AppendHash64From24Bit(output, final[3], final[9], final[15], 4);
        AppendHash64From24Bit(output, final[4], final[10], final[5], 4);
        AppendHash64From24Bit(output, 0, 0, final[11], 2);
        return output.ToString();
#pragma warning restore CA5351
    }

    private static string CreateShaCryptHash(ReadOnlySpan<char> password, ReadOnlySpan<char> salt, int rounds, bool roundsCustom, bool useSha512)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password.ToString());
        var saltString = salt.ToString();
        var saltBytes = Encoding.ASCII.GetBytes(saltString);
        var finalDigest = ComputeShaCryptDigest(passwordBytes, saltBytes, rounds, useSha512);

        var output = new StringBuilder();
        output.Append(useSha512 ? Sha512CryptPrefix : Sha256CryptPrefix);
        if (roundsCustom)
        {
            output.Append(ShaCryptRoundsPrefix);
            output.Append(rounds.ToString(CultureInfo.InvariantCulture));
            output.Append('$');
        }

        output.Append(saltString);
        output.Append('$');
        if (useSha512)
        {
            AppendSha512Digest(output, finalDigest);
        }
        else
        {
            AppendSha256Digest(output, finalDigest);
        }

        return output.ToString();
    }

    private static byte[] ComputeShaCryptDigest(byte[] password, byte[] salt, int rounds, bool useSha512)
    {
        var hashAlgorithm = useSha512 ? HashAlgorithmName.SHA512 : HashAlgorithmName.SHA256;
        var digestLength = useSha512 ? 64 : 32;

        using var hasher = IncrementalHash.CreateHash(hashAlgorithm);

        hasher.AppendData(password);
        hasher.AppendData(salt);
        hasher.AppendData(password);
        var digest = hasher.GetHashAndReset();

        hasher.AppendData(password);
        hasher.AppendData(salt);
        for (var remaining = password.Length; remaining > 0; remaining -= digestLength)
        {
            hasher.AppendData(digest.AsSpan(0, Math.Min(digestLength, remaining)));
        }

        for (var length = password.Length; length > 0; length >>= 1)
        {
            if ((length & 1) != 0)
            {
                hasher.AppendData(digest);
            }
            else
            {
                hasher.AppendData(password);
            }
        }

        digest = hasher.GetHashAndReset();

        for (var i = 0; i < password.Length; i++)
        {
            hasher.AppendData(password);
        }

        var digestP = hasher.GetHashAndReset();
        var sequenceP = ExpandBytes(digestP, password.Length);

        for (var i = 0; i < 16 + digest[0]; i++)
        {
            hasher.AppendData(salt);
        }

        var digestS = hasher.GetHashAndReset();
        var sequenceS = ExpandBytes(digestS, salt.Length);

        var current = digest;
        for (var i = 0; i < rounds; i++)
        {
            if ((i & 1) != 0)
            {
                hasher.AppendData(sequenceP);
            }
            else
            {
                hasher.AppendData(current);
            }

            if (i % 3 != 0)
            {
                hasher.AppendData(sequenceS);
            }

            if (i % 7 != 0)
            {
                hasher.AppendData(sequenceP);
            }

            if ((i & 1) != 0)
            {
                hasher.AppendData(current);
            }
            else
            {
                hasher.AppendData(sequenceP);
            }

            current = hasher.GetHashAndReset();
        }

        return current;
    }

    private static byte[] ExpandBytes(byte[] source, int length)
    {
        if (length == 0)
            return [];

        var result = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var copyLength = Math.Min(source.Length, length - offset);
            source.AsSpan(0, copyLength).CopyTo(result.AsSpan(offset));
            offset += copyLength;
        }

        return result;
    }

    private static void AppendSha256Digest(StringBuilder output, ReadOnlySpan<byte> digest)
    {
        AppendHash64From24Bit(output, digest[0], digest[10], digest[20], 4);
        AppendHash64From24Bit(output, digest[21], digest[1], digest[11], 4);
        AppendHash64From24Bit(output, digest[12], digest[22], digest[2], 4);
        AppendHash64From24Bit(output, digest[3], digest[13], digest[23], 4);
        AppendHash64From24Bit(output, digest[24], digest[4], digest[14], 4);
        AppendHash64From24Bit(output, digest[15], digest[25], digest[5], 4);
        AppendHash64From24Bit(output, digest[6], digest[16], digest[26], 4);
        AppendHash64From24Bit(output, digest[27], digest[7], digest[17], 4);
        AppendHash64From24Bit(output, digest[18], digest[28], digest[8], 4);
        AppendHash64From24Bit(output, digest[9], digest[19], digest[29], 4);
        AppendHash64From24Bit(output, 0, digest[31], digest[30], 3);
    }

    private static void AppendSha512Digest(StringBuilder output, ReadOnlySpan<byte> digest)
    {
        AppendHash64From24Bit(output, digest[0], digest[21], digest[42], 4);
        AppendHash64From24Bit(output, digest[22], digest[43], digest[1], 4);
        AppendHash64From24Bit(output, digest[44], digest[2], digest[23], 4);
        AppendHash64From24Bit(output, digest[3], digest[24], digest[45], 4);
        AppendHash64From24Bit(output, digest[25], digest[46], digest[4], 4);
        AppendHash64From24Bit(output, digest[47], digest[5], digest[26], 4);
        AppendHash64From24Bit(output, digest[6], digest[27], digest[48], 4);
        AppendHash64From24Bit(output, digest[28], digest[49], digest[7], 4);
        AppendHash64From24Bit(output, digest[50], digest[8], digest[29], 4);
        AppendHash64From24Bit(output, digest[9], digest[30], digest[51], 4);
        AppendHash64From24Bit(output, digest[31], digest[52], digest[10], 4);
        AppendHash64From24Bit(output, digest[53], digest[11], digest[32], 4);
        AppendHash64From24Bit(output, digest[12], digest[33], digest[54], 4);
        AppendHash64From24Bit(output, digest[34], digest[55], digest[13], 4);
        AppendHash64From24Bit(output, digest[56], digest[14], digest[35], 4);
        AppendHash64From24Bit(output, digest[15], digest[36], digest[57], 4);
        AppendHash64From24Bit(output, digest[37], digest[58], digest[16], 4);
        AppendHash64From24Bit(output, digest[59], digest[17], digest[38], 4);
        AppendHash64From24Bit(output, digest[18], digest[39], digest[60], 4);
        AppendHash64From24Bit(output, digest[40], digest[61], digest[19], 4);
        AppendHash64From24Bit(output, digest[62], digest[20], digest[41], 4);
        AppendHash64From24Bit(output, 0, 0, digest[63], 2);
    }

    private static void AppendHash64From24Bit(StringBuilder output, int byte2, int byte1, int byte0, int count)
    {
        const string Hash64Characters = "./0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        var value = (uint)((byte2 << 16) | (byte1 << 8) | byte0);
        for (var i = 0; i < count; i++)
        {
            output.Append(Hash64Characters[(int)(value & 0x3f)]);
            value >>= 6;
        }
    }

    private static bool VerifySha1(ReadOnlySpan<char> password, ReadOnlySpan<char> expectedHash)
    {
        var byteCount = Encoding.UTF8.GetByteCount(password);
        byte[]? rentedBytes = null;

        var passwordBytes = byteCount <= 256
            ? stackalloc byte[byteCount]
            : (rentedBytes = ArrayPool<byte>.Shared.Rent(byteCount));

        try
        {
            _ = Encoding.UTF8.GetBytes(password, passwordBytes);

            Span<byte> hashBytes = stackalloc byte[20];
#pragma warning disable CA5350 // SHA1 is required to support the htpasswd {SHA} format
            _ = SHA1.HashData(passwordBytes, hashBytes);
#pragma warning restore CA5350

            Span<char> base64 = stackalloc char[28];
            _ = Convert.TryToBase64Chars(hashBytes, base64, out var charsWritten);

            return expectedHash.SequenceEqual(base64[..charsWritten]);
        }
        finally
        {
            if (rentedBytes is not null)
            {
                ArrayPool<byte>.Shared.Return(rentedBytes);
            }
        }
    }
}