using System.Collections.Concurrent;
using System.Reflection.Metadata;

namespace Meziantou.Framework.Assertions.Architecture.Reading;

// Finds the definition of the types the assemblies of the architecture refer to, in the architecture or in the referenced assemblies.
// The referenced assemblies are searched in the folders of the assemblies of the architecture, in the additional folders,
// then in the assemblies of the running framework, and their type forwarders are followed.
internal sealed class AssemblyResolver
{
    private const int MaxForwarderDepth = 8;

    private static readonly Lazy<Dictionary<string, string>> PlatformAssemblies = new(GetPlatformAssemblies);

    private readonly Dictionary<string, AssemblyImage> _architectureImages = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<AssemblyImage> _architectureImageList;
    private readonly IReadOnlyList<string> _searchDirectories;
    private readonly ConcurrentDictionary<string, Lazy<AssemblyImage?>> _referencedImages = new(StringComparer.OrdinalIgnoreCase);

    public AssemblyResolver(IReadOnlyList<AssemblyImage> architectureImages, IEnumerable<string> searchDirectories)
    {
        _architectureImageList = architectureImages;
        foreach (var image in architectureImages)
        {
            _architectureImages.TryAdd(image.Name, image);
        }

        var directories = new List<string>();
        foreach (var image in architectureImages)
        {
            if (Path.GetDirectoryName(image.FilePath) is { Length: > 0 } directory && !directories.Contains(directory, StringComparer.Ordinal))
            {
                directories.Add(directory);
            }
        }

        foreach (var directory in searchDirectories)
        {
            if (!directories.Contains(directory, StringComparer.Ordinal))
            {
                directories.Add(directory);
            }
        }

        _searchDirectories = directories;
    }

    public AssemblyImage? GetAssembly(string name)
    {
        if (_architectureImages.TryGetValue(name, out var image))
            return image;

        return _referencedImages.GetOrAdd(name, name => new Lazy<AssemblyImage?>(() => OpenReferencedAssembly(name))).Value;
    }

    public ResolvedTypeDefinition? Resolve(AssemblyImage image, EntityHandle handle)
    {
        var reader = image.Reader;
        handle = MetadataNames.GetNamedTypeHandle(reader, handle);
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                return new ResolvedTypeDefinition(image, (TypeDefinitionHandle)handle);

            case HandleKind.TypeReference:
                {
                    var fullName = MetadataNames.GetFullName(reader, handle);

                    // A nested type is declared in the same module as its containing type
                    var typeReference = reader.GetTypeReference((TypeReferenceHandle)handle);
                    while (typeReference.ResolutionScope.Kind == HandleKind.TypeReference)
                    {
                        typeReference = reader.GetTypeReference((TypeReferenceHandle)typeReference.ResolutionScope);
                    }

                    return typeReference.ResolutionScope.Kind switch
                    {
                        HandleKind.AssemblyReference => FindType(reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)typeReference.ResolutionScope).Name), fullName),
                        HandleKind.ModuleDefinition or HandleKind.ModuleReference => FindType(image, fullName, depth: 0),
                        _ => FindType(assemblyName: null, fullName),
                    };
                }

            default:
                return null;
        }
    }

    // Finds a type by its metadata name. When the assembly is unknown or cannot be found, the assemblies of the architecture are searched.
    public ResolvedTypeDefinition? FindType(string? assemblyName, string fullName)
    {
        if (assemblyName is not null && GetAssembly(assemblyName) is { } image && FindType(image, fullName, depth: 0) is { } result)
            return result;

        foreach (var architectureImage in _architectureImageList)
        {
            if (architectureImage.TryGetType(fullName, out var handle))
                return new ResolvedTypeDefinition(architectureImage, handle);
        }

        return null;
    }

    private ResolvedTypeDefinition? FindType(AssemblyImage? image, string fullName, int depth)
    {
        if (image is null || depth > MaxForwarderDepth)
            return null;

        if (image.TryGetType(fullName, out var handle))
            return new ResolvedTypeDefinition(image, handle);

        // The type forwarders of a nested type are the ones of its containing type
        var separatorIndex = fullName.IndexOf('+', StringComparison.Ordinal);
        var topLevelTypeName = separatorIndex < 0 ? fullName : fullName[..separatorIndex];
        if (image.GetForwardedAssemblyName(topLevelTypeName) is { } assemblyName)
            return FindType(GetAssembly(assemblyName), fullName, depth + 1);

        return null;
    }

    private AssemblyImage? OpenReferencedAssembly(string name)
    {
        foreach (var directory in _searchDirectories)
        {
            var image = AssemblyImage.TryOpenMetadataOnly(Path.Combine(directory, name + ".dll")) ?? AssemblyImage.TryOpenMetadataOnly(Path.Combine(directory, name + ".exe"));
            if (image is not null)
                return image;
        }

        if (PlatformAssemblies.Value.TryGetValue(name, out var path))
            return AssemblyImage.TryOpenMetadataOnly(path);

        return null;
    }

    // The assemblies of the running framework
    private static Dictionary<string, string> GetPlatformAssemblies()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string platformAssemblies)
        {
            foreach (var path in platformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                result.TryAdd(Path.GetFileNameWithoutExtension(path), path);
            }
        }

        return result;
    }
}
