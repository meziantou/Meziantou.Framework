using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.SevenZip;

/// <summary>The password and the limits of the decoders, and the AES keys already derived (deriving one is slow).</summary>
internal sealed class SevenZipDecoderContext
{
    private readonly Dictionary<(int CyclesPower, string Salt), byte[]> _keys = [];

    public SevenZipDecoderContext(string? password, long maxMemoryUsage)
    {
        Password = password;
        MaxMemoryUsage = maxMemoryUsage;
    }

    public string? Password { get; }

    public long MaxMemoryUsage { get; }

    public byte[] GetKey(int cyclesPower, byte[] salt)
    {
        if (Password is null)
            throw new ArchivePasswordException("The 7z archive is encrypted and no password is provided.");

        var cacheKey = (cyclesPower, Convert.ToHexString(salt));
        if (!_keys.TryGetValue(cacheKey, out var key))
        {
            key = SevenZipAes.DeriveKey(Password, cyclesPower, salt);
            _keys[cacheKey] = key;
        }

        return key;
    }
}
