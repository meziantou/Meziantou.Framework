using System.Text;
using System.Text.Json;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.PublicApiGenerator.Fuzz;

/// <summary>
/// The <c>.deps.json</c> reader only throws <see cref="InvalidDataException"/>, and only for content that is not valid JSON.
/// The names it returns are assembly names, and writing them back to a <c>.deps.json</c> file gives the same names.
/// Renaming the generated files that have the same name ignoring case gives distinct names, and keeps the first name of each group.
/// </summary>
public static class PublicApiGeneratorFuzzTargets
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly string[] Tokens =
    [
        "{", "}", "[", "]", ":", ",", "\"", "\\", "/", "//", "/*", "*/", "null", "true", "1.0.0",
        "\"targets\"", "\"runtime\"", "\"compile\"", "\"libraries\"", "\"type\"", "\"package\"", "\"project\"", "\"reference\"",
        "\"runtimeTarget\"", "\"dependencies\"", "\"resources\"", "\"native\"", ".dll", ".DLL", ".exe", ".pdb", "_._",
        "lib/netstandard2.0/", "lib/net10.0/", ".NETCoreApp,Version=v10.0", ".NETStandard,Version=v2.0/",
    ];

    private static readonly string[] FileNameTokens =
    [
        "\n", " ", ".", "~", "~2", ".g.cs", ".G.CS", ".cs", ".txt",
        "AssemblyInfo.g.cs", "GlobalNamespace.g.cs", "Demo", "demo", "DEMO", "Foo", "foo",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("publicapi-deps-json", DependencyFile) { Seeds = GetSeeds, Dictionary = Tokens },
        new("publicapi-file-names", FileNames) { Seeds = GetFileNameSeeds, Dictionary = FileNameTokens },
    ];

    private static void FileNames(ReadOnlySpan<byte> data)
    {
        var names = Encoding.UTF8.GetString(data).Split('\n');
        var files = names.Select((name, index) => new PublicApiFile(name, index.ToString(CultureInfo.InvariantCulture))).ToList();
        var result = PublicApiEmitter.MakeFileNamesUnique(files);

        FuzzAssert.Equal(files.Count, result.Count, "Files are added or removed");
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < files.Count; i++)
        {
            var original = files[i].RelativePath;
            var renamed = result[i].RelativePath;
            FuzzAssert.Equal(files[i].Content, result[i].Content, "The files are reordered");
            FuzzAssert.True(usedNames.Add(renamed), $"The name {FuzzAssert.Format(renamed)} is used by several files");
            if (string.Equals(original, renamed, StringComparison.Ordinal))
                continue;

            FuzzAssert.True(files.Take(i).Any(file => string.Equals(file.RelativePath, original, StringComparison.OrdinalIgnoreCase)), $"The file {FuzzAssert.Format(original)} is renamed to {FuzzAssert.Format(renamed)} although no previous file has its name");
            FuzzAssert.True(IsSuffixed(original, renamed), $"The file {FuzzAssert.Format(original)} is renamed to {FuzzAssert.Format(renamed)}, which is not its name with a ~N suffix");
        }
    }

    /// <summary>Gets whether <paramref name="renamed"/> is <paramref name="original"/> with <c>~</c> and a number inserted somewhere.</summary>
    private static bool IsSuffixed(string original, string renamed)
    {
        for (var index = renamed.IndexOf('~', StringComparison.Ordinal); index >= 0; index = renamed.IndexOf('~', index + 1, StringComparison.Ordinal))
        {
            var digitCount = renamed.AsSpan(index + 1).IndexOfAnyExceptInRange('0', '9');
            if (digitCount < 0)
            {
                digitCount = renamed.Length - index - 1;
            }

            if (digitCount > 0 && string.Equals(original, renamed[..index] + renamed[(index + 1 + digitCount)..], StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static IEnumerable<FuzzSeed> GetFileNameSeeds()
    {
        yield return FuzzSeed.FromText("distinct", "AssemblyInfo.g.cs\nDemo.g.cs\nGlobalNamespace.g.cs");
        yield return FuzzSeed.FromText("case-conflicts", "Demo.FOO.g.cs\nDemo.Foo.g.cs\nDemo.foo.g.cs");
        yield return FuzzSeed.FromText("suffix-conflict", "Demo.g.cs\ndemo.g.cs\nDemo~2.g.cs\nSample.cs\nsample.cs\n\n");
    }

    private static void DependencyFile(ReadOnlySpan<byte> data)
    {
        var content = data.ToArray();
        IReadOnlySet<string> names;
        try
        {
            names = DependencyFileReader.ReadExternalAssemblyNames(content);
        }
        catch (InvalidDataException)
        {
            FuzzAssert.False(IsValidJson(content), "A valid JSON document is rejected");
            return;
        }

        FuzzAssert.True(IsValidJson(content), "An invalid JSON document is accepted");
        foreach (var name in names)
        {
            FuzzAssert.True(name.Length > 0, "An assembly name is empty");
            FuzzAssert.True(name.AsSpan().IndexOfAny('/', '\\') < 0, $"The assembly name {FuzzAssert.Format(name)} contains a directory separator");
            FuzzAssert.Equal(name, DependencyFileReader.GetAssemblyName("lib/" + name + ".dll"), "The name is not the name of its asset");
        }

        var roundTrip = DependencyFileReader.ReadExternalAssemblyNames(CreateDependencyFile(names));
        FuzzAssert.True(roundTrip.SetEquals(names), $"Writing the names to a dependency file and reading it again gives {string.Join(", ", roundTrip.Select(FuzzAssert.Format))} instead of {string.Join(", ", names.Select(FuzzAssert.Format))}");
    }

    private static bool IsValidJson(byte[] content)
    {
        try
        {
            using var document = JsonDocument.Parse(content, DocumentOptions);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static byte[] CreateDependencyFile(IEnumerable<string> names)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("targets");
            writer.WriteStartObject(".NETCoreApp,Version=v10.0");
            writer.WriteStartObject("Package/1.0.0");
            writer.WriteStartObject("runtime");
            foreach (var name in names)
            {
                writer.WriteStartObject("lib/net10.0/" + name + ".dll");
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>The dependency files of the unit tests and of the SDK: packages, project references, assets that are not assemblies, invalid JSON.</summary>
    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        yield return FuzzSeed.FromText("empty-object", "{}");
        yield return FuzzSeed.FromText("invalid", """{ "targets": """);
        yield return FuzzSeed.FromText("not-an-object", """[ { "targets": {} } ]""");
        yield return FuzzSeed.FromText("package", """
            {
              "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0", "signature": "" },
              "targets": {
                ".NETCoreApp,Version=v10.0": {
                  "Source/1.0.0": {
                    "dependencies": { "Package": "1.0.0" },
                    "runtime": { "Source.dll": {} }
                  },
                  "Package/1.0.0": {
                    "runtime": {
                      "lib/net10.0/System.Net.Http.dll": { "assemblyVersion": "1.0.0.0", "fileVersion": "1.0.0.0" }
                    }
                  }
                }
              },
              "libraries": {
                "Source/1.0.0": { "type": "project", "serviceable": false, "sha512": "" },
                "Package/1.0.0": { "type": "package", "serviceable": true, "sha512": "sha512-abc", "path": "package/1.0.0" }
              }
            }
            """);
        yield return FuzzSeed.FromText("netstandard-packages", """
            {
              "runtimeTarget": { "name": ".NETStandard,Version=v2.0/", "signature": "" },
              "compilationOptions": {},
              "targets": {
                ".NETStandard,Version=v2.0": {},
                ".NETStandard,Version=v2.0/": {
                  "Meziantou.Framework.Scheduling/1.0.0": {
                    "dependencies": { "System.Memory": "4.6.3" },
                    "runtime": { "Meziantou.Framework.Scheduling.dll": {} }
                  },
                  "System.Buffers/4.6.1": { "runtime": { "lib/netstandard2.0/System.Buffers.dll": { "assemblyVersion": "4.0.5.0", "fileVersion": "4.600.125.16908" } } },
                  "System.Memory/4.6.3": {
                    "dependencies": { "System.Buffers": "4.6.1" },
                    "runtime": { "lib/netstandard2.0/System.Memory.dll": { "assemblyVersion": "4.0.5.0", "fileVersion": "4.600.325.20307" } }
                  },
                  "Native/1.0.0": {
                    "native": { "runtimes/win-x64/native/native.dll": {} },
                    "resources": { "lib/netstandard2.0/fr/Native.resources.dll": { "locale": "fr" } },
                    "compile": { "ref/netstandard2.0/Native.dll": {}, "lib/netstandard2.0/_._": {} }
                  }
                }
              },
              "libraries": {
                "Meziantou.Framework.Scheduling/1.0.0": { "type": "project", "serviceable": false, "sha512": "" },
                "System.Buffers/4.6.1": { "type": "package", "serviceable": true, "sha512": "sha512-x", "path": "system.buffers/4.6.1" },
                "System.Memory/4.6.3": { "type": "package", "serviceable": true, "sha512": "sha512-y", "path": "system.memory/4.6.3" }
              }
            }
            """);
        yield return FuzzSeed.FromText("project-reference", """
            {
              // Comments and trailing commas are accepted
              "targets": {
                ".NETCoreApp,Version=v10.0": {
                  "Demo/1.0.0": { "dependencies": { "Demo.Core": "1.0.0" }, "runtime": { "Demo.dll": {} } },
                  "Demo.Core/1.0.0": { "runtime": { "Demo.Core.dll": {}, }, },
                  "Tool/1.0.0": { "runtime": { "tools\\Tool.EXE": {} } },
                },
              },
            }
            """);
    }
}
