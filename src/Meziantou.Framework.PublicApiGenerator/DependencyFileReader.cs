using System.Text.Json;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>
/// Reads the <c>.deps.json</c> file that the SDK writes next to an assembly. It lists the packages, the project references
/// and the referenced files of the project, but not the assemblies of the framework, so a referenced assembly it does not
/// list comes from the framework.
/// </summary>
internal static class DependencyFileReader
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Gets the path of the <c>.deps.json</c> file of an assembly (e.g. <c>Sample.deps.json</c> for <c>Sample.dll</c>).</summary>
    public static string GetDependencyFilePath(string assemblyPath)
    {
        return Path.ChangeExtension(assemblyPath, ".deps.json");
    }

    /// <summary>Reads the names of the assemblies that do not come from the framework, or returns <see langword="null"/> when the assembly has no <c>.deps.json</c> file.</summary>
    /// <exception cref="InvalidDataException">The file is not valid JSON.</exception>
    public static IReadOnlySet<string>? ReadExternalAssemblyNames(string assemblyPath)
    {
        var path = GetDependencyFilePath(assemblyPath);
        if (!File.Exists(path))
            return null;

        return ReadExternalAssemblyNames(File.ReadAllBytes(path));
    }

    /// <summary>Reads the names of the assemblies listed in the content of a <c>.deps.json</c> file. The names are compared without case, as the runtime does.</summary>
    /// <exception cref="InvalidDataException">The content is not valid JSON.</exception>
    public static IReadOnlySet<string> ReadExternalAssemblyNames(ReadOnlyMemory<byte> utf8Json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Json, DocumentOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The dependency file is not valid JSON.", ex);
        }

        using (document)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (document.RootElement.ValueKind is JsonValueKind.Object && document.RootElement.TryGetProperty("targets", out var targets) && targets.ValueKind is JsonValueKind.Object)
            {
                foreach (var target in targets.EnumerateObject())
                {
                    if (target.Value.ValueKind is not JsonValueKind.Object)
                        continue;

                    // Each library is "<name>/<version>". Its assets are the files it brings, whose names are the names of the assemblies.
                    foreach (var library in target.Value.EnumerateObject())
                    {
                        if (library.Value.ValueKind is not JsonValueKind.Object)
                            continue;

                        AddAssetNames(names, library.Value, "runtime");
                        AddAssetNames(names, library.Value, "compile");
                    }
                }
            }

            return names;
        }
    }

    private static void AddAssetNames(HashSet<string> names, JsonElement library, string propertyName)
    {
        if (!library.TryGetProperty(propertyName, out var assets) || assets.ValueKind is not JsonValueKind.Object)
            return;

        foreach (var asset in assets.EnumerateObject())
        {
            string assetPath;
            try
            {
                assetPath = asset.Name;
            }
            catch (InvalidOperationException)
            {
                // The name contains an escaped unpaired surrogate, so it is not the path of a file
                continue;
            }

            var name = GetAssemblyName(assetPath);
            if (name is not null)
            {
                names.Add(name);
            }
        }
    }

    /// <summary>Gets the assembly name of an asset path (e.g. <c>System.Memory</c> for <c>lib/netstandard2.0/System.Memory.dll</c>), or <see langword="null"/> when the asset is not an assembly.</summary>
    internal static string? GetAssemblyName(string assetPath)
    {
        var fileName = assetPath.AsSpan(assetPath.AsSpan().LastIndexOfAny('/', '\\') + 1);
        if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return null;

        var name = fileName[..^4];
        return name.IsEmpty ? null : name.ToString();
    }
}
