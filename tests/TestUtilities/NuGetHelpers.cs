#if NET
#pragma warning disable MA0042
#endif
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using Meziantou.Framework;

#if NETFRAMEWORK
using System.Net.Http;
#endif

namespace TestUtilities;

public static class NuGetHelpers
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<string[]>>> NuGetPackagesCache = new(StringComparer.Ordinal);

#if NET
    [SuppressMessage("Performance", "MA0106:Avoid closure by using an overload with the 'factoryArgument' parameter", Justification = "Not important in tests")]
#endif
    public static async Task<string[]> GetNuGetReferences(string packageName, string version, params string[] paths)
    {
        var bytes = Encoding.UTF8.GetBytes(packageName + '@' + version + ':' + string.Join(",", paths));
#if NET8_0_OR_GREATER
        var hash = SHA256.HashData(bytes);
#else
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(bytes);
#endif
        var key = Convert.ToBase64String(hash).Replace('/', '_');
        var task = NuGetPackagesCache.GetOrAdd(key, _ => new Lazy<Task<string[]>>(Download));
        return await task.Value.ConfigureAwait(false);

        async Task<string[]> Download()
        {
            var cacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Meziantou.FrameworkTests", "ref", key);
            bool IsCacheValid() => Directory.Exists(cacheFolder) && Directory.EnumerateFileSystemEntries(cacheFolder).Any();

            if (!IsCacheValid())
            {
                var tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

                Directory.CreateDirectory(tempFolder);
                using var stream = await SharedHttpClient.Instance.GetStreamAsync(new Uri($"https://www.nuget.org/api/v2/package/{packageName}/{version}")).ConfigureAwait(false);
                using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

                foreach (var entry in zip.Entries.Where(file => paths.Any(path => file.FullName.StartsWith(path, StringComparison.Ordinal))))
                {
#if NET10_0_OR_GREATER
                    await entry.ExtractToFileAsync(Path.Combine(tempFolder, entry.Name), overwrite: true);
#else
                    entry.ExtractToFile(Path.Combine(tempFolder, entry.Name), overwrite: true);
#endif
                }

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cacheFolder));
                    Directory.Move(tempFolder, cacheFolder);
                }
                catch (Exception ex)
                {
                    if (!IsCacheValid())
                    {
                        throw new InvalidOperationException("Cannot download NuGet package " + packageName + "@" + version + "\n" + ex);
                    }
                }
            }

            var dlls = Directory.GetFiles(cacheFolder, "*.dll");

            // Filter invalid .NET assembly
            var result = new List<string>();
            foreach (var dll in dlls)
            {
                if (Path.GetFileName(dll) == "System.EnterpriseServices.Wrapper.dll")
                    continue;

                try
                {
                    using var stream = File.OpenRead(dll);
                    using var peFile = new PEReader(stream);
                    var metadataReader = peFile.GetMetadataReader();
                    result.Add(dll);
                }
                catch
                {
                }
            }

            if (result is [])
                throw new Exception("No valid assembly found in the NuGet package");

            return [.. result];
        }
    }
}
