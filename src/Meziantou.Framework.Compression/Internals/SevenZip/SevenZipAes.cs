using System.Security.Cryptography;
using System.Text;

namespace Meziantou.Framework.Compression.Internals.SevenZip;

/// <summary>
/// The AES-256 coder of 7z (06F10701): AES-CBC whose key is SHA-256 over 2^NumCyclesPower repetitions of the salt, the
/// UTF-16LE password and an 8-byte little-endian counter.
/// </summary>
internal static class SevenZipAes
{
    public const int KeySize = 32;
    public const int BlockSize = 16;

    /// <summary>Parses the properties of the coder.</summary>
    public static (int CyclesPower, byte[] Salt, byte[] Iv) ParseProperties(ReadOnlySpan<byte> properties)
    {
        if (properties.IsEmpty)
            throw new InvalidDataException("The AES properties of the 7z archive are missing.");

        var first = properties[0];
        var cyclesPower = first & 0x3F;
        if ((first & 0xC0) is 0)
            return (cyclesPower, [], new byte[BlockSize]);

        if (properties.Length < 2)
            throw new InvalidDataException("The AES properties of the 7z archive are truncated.");

        var second = properties[1];
        var saltSize = ((first >> 7) & 1) + (second >> 4);
        var ivSize = ((first >> 6) & 1) + (second & 0x0F);
        if (properties.Length < 2 + saltSize + ivSize)
            throw new InvalidDataException("The AES properties of the 7z archive are truncated.");

        var salt = properties.Slice(2, saltSize).ToArray();
        var iv = new byte[BlockSize];
        properties.Slice(2 + saltSize, ivSize).CopyTo(iv);
        return (cyclesPower, salt, iv);
    }

    /// <summary>The properties 7-Zip writes: no salt, a 16-byte IV, and the number of cycles.</summary>
    public static byte[] CreateProperties(int cyclesPower, ReadOnlySpan<byte> iv)
    {
        var result = new byte[2 + iv.Length];
        result[0] = (byte)(cyclesPower | 0x40);
        result[1] = (byte)(iv.Length - 1);
        iv.CopyTo(result.AsSpan(2));
        return result;
    }

    public static byte[] DeriveKey(string password, int cyclesPower, ReadOnlySpan<byte> salt)
    {
        var passwordBytes = Encoding.Unicode.GetBytes(password);
        if (cyclesPower == SevenZipConstants.RawAesKeyCyclesPower)
        {
            var raw = new byte[KeySize];
            var length = Math.Min(salt.Length, KeySize);
            salt[..length].CopyTo(raw);
            passwordBytes.AsSpan(0, Math.Min(passwordBytes.Length, KeySize - length)).CopyTo(raw.AsSpan(length));
            return raw;
        }

        if (cyclesPower > SevenZipConstants.MaxAesCyclesPower)
            throw new NotSupportedException($"The AES key of the 7z archive is derived with 2^{cyclesPower} rounds, more than the 2^{SevenZipConstants.MaxAesCyclesPower} that are supported.");

        // Hash the rounds in batches: one block is salt + password + counter, and the counters are written in place
        var blockLength = salt.Length + passwordBytes.Length + 8;
        var rounds = 1L << cyclesPower;
        var batch = (int)Math.Min(rounds, Math.Max(1, 64 * 1024 / blockLength));
        var buffer = new byte[batch * blockLength];
        for (var i = 0; i < batch; i++)
        {
            var block = buffer.AsSpan(i * blockLength, blockLength);
            salt.CopyTo(block);
            passwordBytes.CopyTo(block[salt.Length..]);
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (long round = 0; round < rounds; round += batch)
        {
            var count = (int)Math.Min(batch, rounds - round);
            for (var i = 0; i < count; i++)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan((i * blockLength) + blockLength - 8, 8), round + i);
            }

            hash.AppendData(buffer, 0, count * blockLength);
        }

        return hash.GetHashAndReset();
    }
}
