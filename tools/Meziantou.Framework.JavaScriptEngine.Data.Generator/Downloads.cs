#pragma warning disable MA0004 // Use Task.ConfigureAwait
using System.Security.Cryptography;
using Meziantou.Framework;

namespace Meziantou.Framework.JavaScriptEngine.Data.Generator;

internal static class Downloads
{
    // Downloads a pinned file once: it is kept in artifacts/ (ignored by git), and its SHA-256 is checked on every use
    public static async Task<byte[]> DownloadCachedAsync(string url, string expectedSha256, FullPath cacheDirectory)
    {
        var path = cacheDirectory / (expectedSha256 + "-" + Path.GetFileName(new Uri(url).AbsolutePath));
        if (File.Exists(path))
        {
            var content = await File.ReadAllBytesAsync(path);
            if (string.Equals(Convert.ToHexStringLower(SHA256.HashData(content)), expectedSha256, StringComparison.OrdinalIgnoreCase))
                return content;
        }

        var result = await DownloadAsync(url, expectedSha256);
        Directory.CreateDirectory(cacheDirectory);
        await File.WriteAllBytesAsync(path, result);
        return result;
    }

    public static async Task<byte[]> DownloadAsync(string url, string? expectedSha256)
    {
        Console.WriteLine("Downloading " + url);
        var content = await SharedHttpClient.InstanceWithAutoRedirect.GetByteArrayAsync(url);
        if (expectedSha256 is not null)
        {
            var actual = Convert.ToHexStringLower(SHA256.HashData(content));
            if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The SHA-256 of {url} is {actual}, expected {expectedSha256}");
        }

        return content;
    }
}
