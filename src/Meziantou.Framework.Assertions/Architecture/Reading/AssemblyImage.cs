#pragma warning disable MA0012 // Invalid metadata is reported with BadImageFormatException, like System.Reflection.Metadata does
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Meziantou.Framework.Assertions.Architecture.Reading;

// The metadata of an assembly, read from memory so that no file stays open.
// The assemblies of the architecture keep their IL (PEReader), the referenced assemblies only keep their metadata.
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "The readers work on managed memory: there is no handle to release, and the model has no IDisposable on purpose")]
internal sealed class AssemblyImage
{
    // The readers point to memory owned by these objects: they must stay reachable as long as the image is used
    private readonly PEReader? _peReader;
    [SuppressMessage("CodeQuality", "IDE0052:Remove unread private members", Justification = "The MetadataReader points to memory pinned by this object: the field keeps it alive")]
    private readonly MetadataReaderProvider? _metadataProvider;
    private readonly Lazy<MetadataReader?> _pdbReader;
    private readonly Lazy<Dictionary<string, TypeDefinitionHandle>> _types;
    private readonly Lazy<Dictionary<string, string>> _typeForwarders;
    private readonly ConcurrentDictionary<DocumentHandle, string> _documentPaths = new();
    [SuppressMessage("CodeQuality", "IDE0052:Remove unread private members", Justification = "The MetadataReader points to memory pinned by this object: the field keeps it alive")]
    private MetadataReaderProvider? _pdbProvider;

    private AssemblyImage(PEReader? peReader, MetadataReaderProvider? metadataProvider, MetadataReader reader, string? filePath, bool readSourceLocations)
    {
        _peReader = peReader;
        _metadataProvider = metadataProvider;
        Reader = reader;
        FilePath = filePath;
        if (reader.IsAssembly)
        {
            var definition = reader.GetAssemblyDefinition();
            Name = reader.GetString(definition.Name);
            Version = definition.Version;
        }
        else
        {
            Name = filePath is null ? reader.GetString(reader.GetModuleDefinition().Name) : Path.GetFileNameWithoutExtension(filePath);
            Version = new Version(0, 0, 0, 0);
        }

        _pdbReader = new Lazy<MetadataReader?>(() => readSourceLocations ? OpenPdb() : null);
        _types = new Lazy<Dictionary<string, TypeDefinitionHandle>>(CreateTypeMap);
        _typeForwarders = new Lazy<Dictionary<string, string>>(CreateTypeForwarderMap);
    }

    public MetadataReader Reader { get; }

    public string Name { get; }

    public Version Version { get; }

    public string? FilePath { get; }

    public MetadataReader? PdbReader => _pdbReader.Value;

    public ConcurrentDictionary<EntityHandle, TypeReference> TypeReferences { get; } = new();

    public static AssemblyImage FromPEImage(ImmutableArray<byte> image, string? filePath, bool readSourceLocations)
    {
        var peReader = new PEReader(image);
        if (!peReader.HasMetadata)
            throw new BadImageFormatException($"The file '{filePath ?? "<memory>"}' is not a .NET assembly.");

        // Load the image now so that reading the method bodies later does not race
        _ = peReader.GetEntireImage();
        return new AssemblyImage(peReader, metadataProvider: null, peReader.GetMetadataReader(), filePath, readSourceLocations);
    }

    public static AssemblyImage? TryOpenMetadataOnly(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            ImmutableArray<byte> metadata;
            using (var stream = File.OpenRead(path))
            using (var peReader = new PEReader(stream))
            {
                if (!peReader.HasMetadata)
                    return null;

                metadata = peReader.GetMetadata().GetContent();
            }

            var provider = MetadataReaderProvider.FromMetadataImage(metadata);
            return new AssemblyImage(peReader: null, provider, provider.GetMetadataReader(), path, readSourceLocations: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            return null;
        }
    }

    public MethodBodyBlock? GetMethodBody(MethodDefinition method)
    {
        if (_peReader is null || method.RelativeVirtualAddress == 0)
            return null;

        try
        {
            return _peReader.GetMethodBody(method.RelativeVirtualAddress);
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    public bool TryGetType(string fullName, out TypeDefinitionHandle handle) => _types.Value.TryGetValue(fullName, out handle);

    public string? GetForwardedAssemblyName(string topLevelTypeFullName) => _typeForwarders.Value.GetValueOrDefault(topLevelTypeFullName);

    public string GetDocumentPath(MetadataReader pdbReader, DocumentHandle handle)
    {
        return _documentPaths.GetOrAdd(handle, static (handle, pdbReader) => pdbReader.GetString(pdbReader.GetDocument(handle).Name), pdbReader);
    }

    public override string ToString() => Name;

    private MetadataReader? OpenPdb()
    {
        if (_peReader is null)
            return null;

        try
        {
            // The embedded PDB is read first; the PDB next to the file is read into memory so that no file stays open
            var filePath = FilePath;
            MetadataReaderProvider? provider = null;
            try
            {
                if (_peReader.TryOpenAssociatedPortablePdb(filePath ?? (Name + ".dll"), path => filePath is not null && File.Exists(path) ? new MemoryStream(File.ReadAllBytes(path)) : null, out provider, out _) && provider is not null)
                {
                    var reader = provider.GetMetadataReader();
                    _pdbProvider = provider;
                    provider = null;
                    return reader;
                }
            }
            finally
            {
                provider?.Dispose();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException)
        {
        }

        return null;
    }

    private Dictionary<string, TypeDefinitionHandle> CreateTypeMap()
    {
        var result = new Dictionary<string, TypeDefinitionHandle>(StringComparer.Ordinal);
        foreach (var handle in Reader.TypeDefinitions)
        {
            result.TryAdd(MetadataNames.GetFullName(Reader, handle), handle);
        }

        return result;
    }

    // Full name of the top-level types that are forwarded to another assembly
    private Dictionary<string, string> CreateTypeForwarderMap()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var handle in Reader.ExportedTypes)
        {
            var exportedType = Reader.GetExportedType(handle);
            if (!exportedType.IsForwarder || exportedType.Implementation.Kind != HandleKind.AssemblyReference)
                continue;

            var name = Reader.GetString(exportedType.Name);
            var namespaceName = exportedType.Namespace.IsNil ? string.Empty : Reader.GetString(exportedType.Namespace);
            var assemblyName = Reader.GetString(Reader.GetAssemblyReference((AssemblyReferenceHandle)exportedType.Implementation).Name);
            result.TryAdd(namespaceName.Length == 0 ? name : namespaceName + "." + name, assemblyName);
        }

        return result;
    }
}
