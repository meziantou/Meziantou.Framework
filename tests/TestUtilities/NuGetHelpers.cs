#if !NET462
#pragma warning disable MA0042
#endif
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace TestUtilities;

public static class NuGetHelpers
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<string[]>>> Cache = new(StringComparer.Ordinal);

#if !NET462
    [SuppressMessage("Performance", "MA0106:Avoid closure by using an overload with the 'factoryArgument' parameter", Justification = "Not important in tests")]
#endif
    public static Task<string[]> GetNuGetReferences(string packageName, string version, string path)
    {
        var task = Cache.GetOrAdd(packageName + '@' + version + ':' + path, key =>
        {
            return new Lazy<Task<string[]>>(Download);
        });

        return task.Value;

        async Task<string[]> Download()
        {
            var tempFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Meziantou.AnalyzerTests", "ref", packageName + '@' + version);
            if (!Directory.Exists(tempFolder) || !Directory.EnumerateFileSystemEntries(tempFolder).Any())
            {
                Directory.CreateDirectory(tempFolder);
                using var httpClient = new HttpClient();
                using var stream = await httpClient.GetStreamAsync(new Uri($"https://www.nuget.org/api/v2/package/{packageName}/{version}")).ConfigureAwait(false);
                using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

                foreach (var entry in zip.Entries.Where(file => file.FullName.StartsWith(path, StringComparison.Ordinal)))
                {
                    entry.ExtractToFile(Path.Combine(tempFolder, entry.Name), overwrite: true);
                }
            }

            var dlls = Directory.GetFiles(tempFolder, "*.dll");

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

            return result.ToArray();
        }
    }
}
