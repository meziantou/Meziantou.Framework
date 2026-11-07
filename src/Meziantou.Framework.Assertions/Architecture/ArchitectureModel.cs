using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using Meziantou.Framework.Assertions.Architecture.Reading;

namespace Meziantou.Framework.Assertions.Architecture;

/// <summary>
/// The types of a set of assemblies, read from their files with <c>System.Reflection.Metadata</c>: the assemblies are not loaded for execution.
/// The model is immutable, holds no file handle, and is safe to use from several threads, so it can be stored in a static field and shared by the tests.
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Assertions.Tests/Architecture/DocumentationTests.cs" ref:region="Architecture_DoesNotDependOn" ref:dedent="true">
/// var architecture = ArchitectureModel.FromAssemblyContaining&lt;Order&gt;();
/// var domain = architecture.Types.Where(t =&gt; t.IsInNamespaceOrChildren("SampleApp.Domain"));
/// var infrastructure = architecture.Types.Where(t =&gt; t.IsInNamespaceOrChildren("SampleApp.Infrastructure"));
///
/// Assert.Architecture.DoesNotDependOn(domain, infrastructure, "the domain stays persistence-ignorant");
/// Assert.Architecture.DoesNotDependOn(domain, t =&gt; t.IsInNamespaceOrChildren("Microsoft.EntityFrameworkCore"));
/// Assert.Architecture.DoesNotDependOn&lt;HttpClient&gt;(domain);
/// </code>
/// </example>
public sealed class ArchitectureModel
{
    private readonly List<TypeElement> _types = [];
    private readonly Dictionary<string, List<TypeElement>> _typesByFullName = new(StringComparer.Ordinal);
    private readonly HashSet<string> _compilerGeneratedTypes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<AssemblyImage, TypeReferenceProvider> _providers = new();
    private readonly ConcurrentDictionary<PrimitiveTypeCode, TypeReference> _primitiveTypes = new();

    private ArchitectureModel(IReadOnlyList<AssemblyImage> images, IEnumerable<string> referenceSearchDirectories)
    {
        Resolver = new AssemblyResolver(images, referenceSearchDirectories);
        SystemType = TypeReference.CreateNamed(this, "System", "Type", assemblyName: null, declaringType: null);

        var assemblies = new List<AssemblyElement>();
        foreach (var image in images)
        {
            var assembly = new AssemblyElement(this, image);
            assemblies.Add(assembly);
            assembly.Types = LoadTypes(assembly);
        }

        Assemblies = assemblies;
    }

    internal AssemblyResolver Resolver { get; }

    internal TypeReference SystemType { get; }

    /// <summary>Gets the assemblies of the architecture.</summary>
    public IReadOnlyList<AssemblyElement> Assemblies { get; }

    /// <summary>
    /// Gets all the types of the architecture, including the nested types. The types the compiler generates (closures, state machines, anonymous types)
    /// are not included: their code belongs to the type that declares the lambda, the local function, the iterator or the async method.
    /// Filter them with LINQ and the members of <see cref="TypeElement"/>, such as <c>architecture.Types.Where(t =&gt; t.IsInNamespaceOrChildren("MyApp.Domain"))</c>.
    /// </summary>
    public IReadOnlyList<TypeElement> Types => _types;

    /// <summary>Loads the assemblies from the files they were loaded from.</summary>
    /// <param name="assemblies">The assemblies. An assembly that has no file (single-file application, assembly loaded from memory) cannot be read: use <see cref="FromBytes"/>.</param>
    /// <returns>The architecture.</returns>
    /// <exception cref="ArgumentException">An assembly has no file.</exception>
    public static ArchitectureModel FromAssemblies(params IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var options = new ArchitectureLoadOptions();
        foreach (var assembly in assemblies)
        {
            ArgumentNullException.ThrowIfNull(assembly, nameof(assemblies));
            if (string.IsNullOrEmpty(assembly.Location))
                throw new ArgumentException($"The assembly '{assembly.FullName}' was not loaded from a file. Load its content with ArchitectureModel.FromBytes.", nameof(assemblies));

            options.AssemblyPaths.Add(assembly.Location);
        }

        return Load(options);
    }

    /// <summary>Loads the assembly that defines <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">A type of the assembly.</typeparam>
    /// <returns>The architecture.</returns>
    public static ArchitectureModel FromAssemblyContaining<T>() => FromAssemblies(typeof(T).Assembly);

    /// <summary>Loads assemblies from files.</summary>
    /// <param name="paths">The paths of the assemblies.</param>
    /// <returns>The architecture.</returns>
    public static ArchitectureModel FromFiles(params IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var options = new ArchitectureLoadOptions();
        foreach (var path in paths)
        {
            options.AssemblyPaths.Add(path);
        }

        return Load(options);
    }

    /// <summary>Loads assemblies from memory, such as assemblies compiled at run time.</summary>
    /// <param name="images">The content of the assemblies (PE images). An embedded PDB is read from the image.</param>
    /// <returns>The architecture.</returns>
    public static ArchitectureModel FromBytes(params IEnumerable<ReadOnlyMemory<byte>> images)
    {
        ArgumentNullException.ThrowIfNull(images);

        var options = new ArchitectureLoadOptions();
        foreach (var image in images)
        {
            options.AssemblyImages.Add(image);
        }

        return Load(options);
    }

    /// <summary>Loads assemblies with the given options.</summary>
    /// <param name="options">The assemblies to load and the loading options.</param>
    /// <returns>The architecture.</returns>
    /// <exception cref="BadImageFormatException">A file is not a .NET assembly.</exception>
    public static ArchitectureModel Load(ArchitectureLoadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var images = new List<AssemblyImage>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in options.AssemblyPaths)
        {
            var fullPath = Path.GetFullPath(path);
            AddImage(AssemblyImage.FromPEImage(ImmutableCollectionsMarshal.AsImmutableArray(File.ReadAllBytes(fullPath)), fullPath, options.ReadSourceLocations));
        }

        foreach (var image in options.AssemblyImages)
        {
            AddImage(AssemblyImage.FromPEImage([.. image.Span], filePath: null, options.ReadSourceLocations));
        }

        return new ArchitectureModel(images, options.ReferenceSearchDirectories);

        // An assembly loaded twice is read once
        void AddImage(AssemblyImage image)
        {
            if (names.Add(image.Name))
            {
                images.Add(image);
            }
        }
    }

    /// <summary>Finds a type of the architecture by its full name as written in metadata.</summary>
    /// <param name="fullName">The full name of the type: <c>MyApp.Order</c>, <c>MyApp.Outer+Inner</c>, <c>MyApp.Repository`1</c>.</param>
    /// <returns>The type, or <see langword="null"/> when no assembly of the architecture defines it.</returns>
    public TypeElement? FindType(string fullName)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        return _typesByFullName.TryGetValue(fullName, out var types) ? types[0] : null;
    }

    /// <summary>Finds the type of the architecture that corresponds to a <see cref="Type"/>.</summary>
    /// <param name="type">The type, or a generic instantiation of it.</param>
    /// <returns>The type, or <see langword="null"/> when no assembly of the architecture defines it.</returns>
    public TypeElement? FindType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var types = _typesByFullName.GetValueOrDefault(MetadataNames.GetFullName(type));
        return types?.Find(element => string.Equals(element.Assembly.Name, type.Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase)) ?? types?[0];
    }

    /// <summary>Gets the type of the architecture that corresponds to a <see cref="Type"/>.</summary>
    /// <param name="type">The type, or a generic instantiation of it.</param>
    /// <returns>The type.</returns>
    /// <exception cref="ArgumentException">No assembly of the architecture defines the type.</exception>
    public TypeElement GetTypeElement(Type type) => FindType(type) ?? throw new ArgumentException($"The type '{type}' is not part of the architecture.", nameof(type));

    internal TypeElement? FindType(TypeReference reference)
    {
        if (!_typesByFullName.TryGetValue(reference.FullName, out var types))
            return null;

        if (types.Count > 1 && reference.AssemblyName is not null)
        {
            var match = types.Find(type => string.Equals(type.Assembly.Name, reference.AssemblyName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        return types[0];
    }

    internal bool IsCompilerGeneratedType(string fullName) => _compilerGeneratedTypes.Contains(fullName);

    internal TypeReferenceProvider GetProvider(AssemblyImage image) => _providers.GetOrAdd(image, image => new TypeReferenceProvider(this, image));

    internal TypeReference GetPrimitiveType(PrimitiveTypeCode typeCode)
    {
        return _primitiveTypes.GetOrAdd(typeCode, (typeCode, architecture) => TypeReference.CreateNamed(architecture, "System", GetPrimitiveTypeName(typeCode), assemblyName: null, declaringType: null), this);
    }

    private List<TypeElement> LoadTypes(AssemblyElement assembly)
    {
        var reader = assembly.Image.Reader;
        var classifications = new Dictionary<TypeDefinitionHandle, TypeClassification>();
        var result = new List<TypeElement>();
        foreach (var handle in reader.TypeDefinitions)
        {
            Classify(handle);
        }

        return result;

        TypeClassification Classify(TypeDefinitionHandle handle)
        {
            if (classifications.TryGetValue(handle, out var classification))
                return classification;

            var definition = reader.GetTypeDefinition(handle);
            var name = reader.GetString(definition.Name);
            var declaringTypeHandle = definition.GetDeclaringType();
            if (declaringTypeHandle.IsNil)
            {
                classification = IsCompilerGeneratedTopLevelType(reader, definition, name)
                    ? TypeClassification.Excluded
                    : TypeClassification.FromElement(new TypeElement(this, assembly, handle, declaringType: null));
            }
            else
            {
                var parent = Classify(declaringTypeHandle);
                if (parent.IsExcluded)
                {
                    classification = TypeClassification.Excluded;
                }
                else if (parent.Element is { } parentElement && !IsCompilerGeneratedNestedType(reader, definition, name))
                {
                    var element = new TypeElement(this, assembly, handle, parentElement);
                    parentElement.AddNestedType(element);
                    classification = TypeClassification.FromElement(element);
                }
                else
                {
                    // The code generated for a member belongs to the type that declares the member
                    var owner = parent.Element ?? parent.Owner!;
                    owner.GeneratedTypes.Add(handle);
                    classification = TypeClassification.FromOwner(owner);
                }
            }

            if (classification.Element is { } typeElement)
            {
                _types.Add(typeElement);
                result.Add(typeElement);
                ref var types = ref CollectionsMarshal.GetValueRefOrAddDefault(_typesByFullName, typeElement.FullName, out _);
                (types ??= []).Add(typeElement);
            }
            else
            {
                _compilerGeneratedTypes.Add(MetadataNames.GetFullName(reader, handle));
            }

            classifications.Add(handle, classification);
            return classification;
        }
    }

    // <Module>, <PrivateImplementationDetails>, anonymous types (<>f__AnonymousType0), and the attributes the compiler embeds (NullableAttribute...).
    // File-local types ("<File>F1__Name") are written by the developer: they are part of the architecture.
    private static bool IsCompilerGeneratedTopLevelType(MetadataReader reader, TypeDefinition definition, string name)
    {
        return name is "<Module>"
            || name.StartsWith("<>", StringComparison.Ordinal)
            || name.StartsWith("<PrivateImplementationDetails>", StringComparison.Ordinal)
            || MetadataNames.HasAttribute(reader, definition.GetCustomAttributes(), "System.Runtime.CompilerServices.CompilerGeneratedAttribute");
    }

    // Closures, state machines, and C# 14 extension blocks
    private static bool IsCompilerGeneratedNestedType(MetadataReader reader, TypeDefinition definition, string name)
    {
        return MetadataNames.IsGeneratedName(name)
            || MetadataNames.HasAttribute(reader, definition.GetCustomAttributes(), "System.Runtime.CompilerServices.CompilerGeneratedAttribute");
    }

    private static string GetPrimitiveTypeName(PrimitiveTypeCode typeCode)
    {
        return typeCode switch
        {
            PrimitiveTypeCode.Boolean => "Boolean",
            PrimitiveTypeCode.Byte => "Byte",
            PrimitiveTypeCode.SByte => "SByte",
            PrimitiveTypeCode.Char => "Char",
            PrimitiveTypeCode.Int16 => "Int16",
            PrimitiveTypeCode.UInt16 => "UInt16",
            PrimitiveTypeCode.Int32 => "Int32",
            PrimitiveTypeCode.UInt32 => "UInt32",
            PrimitiveTypeCode.Int64 => "Int64",
            PrimitiveTypeCode.UInt64 => "UInt64",
            PrimitiveTypeCode.Single => "Single",
            PrimitiveTypeCode.Double => "Double",
            PrimitiveTypeCode.IntPtr => "IntPtr",
            PrimitiveTypeCode.UIntPtr => "UIntPtr",
            PrimitiveTypeCode.Object => "Object",
            PrimitiveTypeCode.String => "String",
            PrimitiveTypeCode.TypedReference => "TypedReference",
            PrimitiveTypeCode.Void => "Void",
            _ => typeCode.ToString(),
        };
    }

    private readonly record struct TypeClassification(TypeElement? Element, TypeElement? Owner, bool IsExcluded)
    {
        public static TypeClassification Excluded => new(Element: null, Owner: null, IsExcluded: true);

        public static TypeClassification FromElement(TypeElement element) => new(element, Owner: null, IsExcluded: false);

        public static TypeClassification FromOwner(TypeElement owner) => new(Element: null, owner, IsExcluded: false);
    }
}
