using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>
/// Derives the AES keys of a RAR archive from its password, and caches them by salt: the key derivations are slow on
/// purpose (2^18 SHA-1 rounds for RAR 2.9, 2^15 PBKDF2-HMAC-SHA256 iterations by default for RAR5).
/// </summary>
internal sealed class RarKeyStore
{
    /// <summary>The largest binary logarithm of the PBKDF2 iteration count of RAR5 (unrar has the same limit).</summary>
    public const int MaxRar5KdfCount = 24;

    /// <summary>Gets or sets the largest binary logarithm of the PBKDF2 iteration count that is accepted. The fuzz targets lower it.</summary>
    internal static int MaxKdfCount { get; set; } = MaxRar5KdfCount;

    private const int Rar3Rounds = 0x40000;

    private readonly Dictionary<string, (byte[] Key, byte[] Iv)> _rar3Keys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Rar5Key> _rar5Keys = new(StringComparer.Ordinal);

    public RarKeyStore(string? password)
    {
        Password = password;
    }

    public string? Password { get; }

    /// <summary>Gets the AES-128 key and IV of RAR 2.9 for a salt of 8 bytes, or no salt.</summary>
    public (byte[] Key, byte[] Iv) GetRar3Key(ReadOnlySpan<byte> salt)
    {
        var password = Password ?? throw new ArchivePasswordException("The archive is encrypted and no password is provided.");
        var cacheKey = Convert.ToHexString(salt);
        if (!_rar3Keys.TryGetValue(cacheKey, out var result))
        {
            result = DeriveRar3Key(password, salt);
            _rar3Keys.Add(cacheKey, result);
        }

        return result;
    }

    /// <summary>Gets the AES-256 key, the hash key and the password check value of RAR5.</summary>
    public Rar5Key GetRar5Key(ReadOnlySpan<byte> salt, int kdfCount)
    {
        var password = Password ?? throw new ArchivePasswordException("The archive is encrypted and no password is provided.");
        if (kdfCount > MaxKdfCount)
            throw new InvalidDataException($"The key derivation of the archive uses 2^{kdfCount} iterations, more than the maximum, 2^{MaxKdfCount}.");

        var cacheKey = Convert.ToHexString(salt) + ":" + kdfCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!_rar5Keys.TryGetValue(cacheKey, out var result))
        {
            result = DeriveRar5Key(password, salt, kdfCount);
            _rar5Keys.Add(cacheKey, result);
        }

        return result;
    }

    internal static (byte[] Key, byte[] Iv) DeriveRar3Key(string password, ReadOnlySpan<byte> salt)
    {
        var passwordBytes = Encoding.Unicode.GetBytes(password);
        var raw = new byte[passwordBytes.Length + salt.Length];
        passwordBytes.CopyTo(raw, 0);
        salt.CopyTo(raw.AsSpan(passwordBytes.Length));

        var iv = new byte[16];
        byte[] digest;

        // unrar hashes the password with a SHA-1 that, for the data longer than 64 bytes, writes its message schedule back
        // into the data, which changes the next rounds: a standard SHA-1 gives the same result for shorter passwords
        if (raw.Length >= 65)
        {
            digest = DeriveRar3KeyLongPassword(raw, iv);
        }
        else
        {
            using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            var chunk = new byte[(raw.Length + 3) * 256];
            var chunkLength = 0;
            Span<byte> snapshot = stackalloc byte[20];
            for (var i = 0; i < Rar3Rounds; i++)
            {
                raw.CopyTo(chunk, chunkLength);
                chunkLength += raw.Length;
                chunk[chunkLength++] = (byte)i;
                chunk[chunkLength++] = (byte)(i >> 8);
                chunk[chunkLength++] = (byte)(i >> 16);
                if (i % (Rar3Rounds / 16) == 0 || chunkLength == chunk.Length)
                {
                    sha1.AppendData(chunk, 0, chunkLength);
                    chunkLength = 0;
                }

                if (i % (Rar3Rounds / 16) == 0)
                {
                    sha1.GetCurrentHash(snapshot);
                    iv[i / (Rar3Rounds / 16)] = snapshot[19];
                }
            }

            sha1.AppendData(chunk, 0, chunkLength);
            digest = sha1.GetHashAndReset();
        }

        var key = new byte[16];
        for (var i = 0; i < 4; i++)
        {
            for (var j = 0; j < 4; j++)
            {
                key[(i * 4) + j] = digest[(i * 4) + 3 - j];
            }
        }

        return (key, iv);
    }

    private static byte[] DeriveRar3KeyLongPassword(byte[] raw, byte[] iv)
    {
        var sha1 = new Rar29Sha1();
        Span<byte> counter = stackalloc byte[3];
        Span<byte> snapshot = stackalloc byte[20];
        for (var i = 0; i < Rar3Rounds; i++)
        {
            sha1.AppendRar29(raw);
            counter[0] = (byte)i;
            counter[1] = (byte)(i >> 8);
            counter[2] = (byte)(i >> 16);
            sha1.Append(counter);
            if (i % (Rar3Rounds / 16) == 0)
            {
                sha1.Clone().GetHash(snapshot);
                iv[i / (Rar3Rounds / 16)] = snapshot[19];
            }
        }

        var digest = new byte[20];
        sha1.GetHash(digest);
        return digest;
    }

    internal static Rar5Key DeriveRar5Key(string password, ReadOnlySpan<byte> salt, int kdfCount)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var iterations = 1 << kdfCount;
        var key = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, iterations, HashAlgorithmName.SHA256, 32);
        var hashKey = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, iterations + 16, HashAlgorithmName.SHA256, 32);
        var checkValue = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, iterations + 32, HashAlgorithmName.SHA256, 32);
        var passwordCheck = new byte[8];
        for (var i = 0; i < checkValue.Length; i++)
        {
            passwordCheck[i % 8] ^= checkValue[i];
        }

        return new Rar5Key(key, hashKey, passwordCheck);
    }

    /// <summary>Checks that the 4 bytes after the 8 bytes of a password check value are its checksum, which tells a damaged value from a wrong password.</summary>
    public static bool IsPasswordCheckValid(ReadOnlySpan<byte> checkValue)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(checkValue[..8], hash);
        return hash[..4].SequenceEqual(checkValue.Slice(8, 4));
    }

    /// <summary>Converts the CRC-32 of decrypted data into the "tweaked" CRC that RAR5 stores when the hashes depend on the key.</summary>
    public static uint ConvertCrc(uint crc, ReadOnlySpan<byte> hashKey)
    {
        Span<byte> raw = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(raw, crc);
        Span<byte> digest = stackalloc byte[32];
        HMACSHA256.HashData(hashKey, raw, digest);
        uint result = 0;
        for (var i = 0; i < digest.Length; i++)
        {
            result ^= (uint)digest[i] << ((i & 3) * 8);
        }

        return result;
    }

    /// <summary>Converts the BLAKE2sp hash of decrypted data into the hash that RAR5 stores when the hashes depend on the key.</summary>
    public static byte[] ConvertBlake2(ReadOnlySpan<byte> hash, ReadOnlySpan<byte> hashKey)
    {
        return HMACSHA256.HashData(hashKey, hash);
    }
}
