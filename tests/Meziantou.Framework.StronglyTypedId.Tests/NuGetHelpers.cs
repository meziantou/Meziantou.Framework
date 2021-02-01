﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Threading.Tasks;

namespace Meziantou.Framework.StronglyTypedId.Tests
{
    internal static class NuGetHelpers
    {
        private static readonly ConcurrentDictionary<string, Lazy<Task<string[]>>> s_cache = new(StringComparer.Ordinal);

        public static Task<string[]> GetNuGetReferences(string packageName, string version, string path)
        {
            var task = s_cache.GetOrAdd(packageName + '@' + version + ':' + path, key =>
            {
                return new Lazy<Task<string[]>>(Download);
            });

            return task.Value;

            async Task<string[]> Download()
            {
                var tempFolder = Path.Combine(Path.GetTempPath(), "Meziantou.AnalyzerTests", "ref", packageName + '@' + version);
                if (!Directory.Exists(tempFolder) || !Directory.EnumerateFileSystemEntries(tempFolder).Any())
                {
                    Directory.CreateDirectory(tempFolder);
                    using var httpClient = new HttpClient();
                    await using var stream = await httpClient.GetStreamAsync(new Uri($"https://www.nuget.org/api/v2/package/{packageName}/{version}")).ConfigureAwait(false);
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
                        await using var stream = File.OpenRead(dll);
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
}
