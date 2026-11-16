using System.IO.Compression;
using System.Security.Cryptography;
using Meziantou.Framework.Compression.Internals.Crypto;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.SevenZip;

/// <summary>The coders the writer uses for the options of an archive, and the AES key (derived once: it is slow).</summary>
internal sealed class SevenZipWriterSettings
{
    private static readonly string[] ExecutableExtensions = [".exe", ".dll", ".sys", ".ocx", ".scr", ".cpl", ".com", ".efi", ".drv"];

    private byte[]? _key;

    public SevenZipWriterSettings(SevenZipCompressionOptions options)
    {
        Options = options;
        if (options.EncryptHeaders && string.IsNullOrEmpty(options.Password))
            throw new ArgumentException("The headers can be encrypted only with a password.", nameof(options));
    }

    public SevenZipCompressionOptions Options { get; }

    public bool IsEncrypted => !string.IsNullOrEmpty(Options.Password);

    /// <summary>Gets the filter of an entry, from its name for <see cref="SevenZipFilter.Auto"/>.</summary>
    public SevenZipFilter ResolveFilter(string entryName)
    {
        if (Options.Filter is not SevenZipFilter.Auto)
            return Options.Filter;

        if (GetMethod() is SevenZipCompressionMethod.Copy)
            return SevenZipFilter.None;

        foreach (var extension in ExecutableExtensions)
        {
            if (entryName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return SevenZipFilter.X86;
        }

        return SevenZipFilter.None;
    }

    /// <summary>Gets the method: level 0 stores the data, as 7-Zip does.</summary>
    public SevenZipCompressionMethod GetMethod() => Options.Level is 0 ? SevenZipCompressionMethod.Copy : Options.Method;

    /// <summary>Gets the coders of a folder of content: the filter, the compression, and the encryption.</summary>
    public List<SevenZipEncoderSpec> GetContentSpecs(SevenZipFilter filter)
    {
        var specs = new List<SevenZipEncoderSpec>();
        if (GetFilterSpec(filter) is { } filterSpec)
        {
            specs.Add(filterSpec);
        }

        specs.Add(GetMethodSpec(GetMethod(), Options.Level, Options.DictionarySize));
        if (IsEncrypted)
        {
            specs.Add(GetAesSpec());
        }

        return specs;
    }

    /// <summary>Gets the coders of an encoded header: LZMA (as 7-Zip), or Copy, then the encryption.</summary>
    public List<SevenZipEncoderSpec> GetHeaderSpecs(long headerLength)
    {
        var specs = new List<SevenZipEncoderSpec>();
        if (Options.CompressHeaders)
        {
            specs.Add(SevenZipCoders.CreateHeaderEncoderSpec(headerLength));
        }
        else
        {
            // The AES output is padded: the Copy coder gives the length of the header
            specs.Add(SevenZipEncoderSpec.Copy);
        }

        if (Options.EncryptHeaders)
        {
            specs.Add(GetAesSpec());
        }

        return specs;
    }

    private static SevenZipEncoderSpec? GetFilterSpec(SevenZipFilter filter)
    {
        return filter switch
        {
            SevenZipFilter.None or SevenZipFilter.Auto => null,
            _ => SevenZipCoders.CreateFilterEncoderSpec(filter),
        };
    }

    private static SevenZipEncoderSpec GetMethodSpec(SevenZipCompressionMethod method, int level, int? dictionarySize)
    {
        return method switch
        {
            SevenZipCompressionMethod.Copy => SevenZipEncoderSpec.Copy,
            SevenZipCompressionMethod.Deflate => new SevenZipEncoderSpec(SevenZipMethodId.Deflate, output => (new DeflateStream(output, GetDeflateLevel(level), leaveOpen: false), () => [])),
            _ => SevenZipCoders.CreateMethodEncoderSpec(method, level, dictionarySize),
        };
    }

    private static CompressionLevel GetDeflateLevel(int level) => level switch
    {
        <= 2 => CompressionLevel.Fastest,
        >= 8 => CompressionLevel.SmallestSize,
        _ => CompressionLevel.Optimal,
    };

    private SevenZipEncoderSpec GetAesSpec()
    {
        _key ??= SevenZipAes.DeriveKey(Options.Password!, SevenZipConstants.DefaultAesCyclesPower, []);
        var key = _key;
        return new SevenZipEncoderSpec(SevenZipMethodId.Aes, output =>
        {
            var iv = RandomNumberGenerator.GetBytes(SevenZipAes.BlockSize);
            var properties = SevenZipAes.CreateProperties(SevenZipConstants.DefaultAesCyclesPower, iv);
            return (new AesCbcEncryptStream(output, key, iv, leaveOpen: false), () => properties);
        });
    }
}
