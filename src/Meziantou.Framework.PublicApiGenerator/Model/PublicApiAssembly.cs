using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>The public API of an assembly, read from its metadata.</summary>
/// <remarks>
/// The model only contains data decoded from metadata: it does not reference the file, the stream or the metadata reader used to create it,
/// and it remains valid after they are disposed. Reading an assembly never loads or executes it, nor the assemblies it references.
/// </remarks>
public sealed class PublicApiAssembly
{
    private readonly ImmutableArray<PublicApiSymbol> _symbols;
    private readonly Dictionary<string, PublicApiSymbol> _symbolsByDocumentationId;
    private Dictionary<string, PublicApiType>? _typesByFullName;

    internal PublicApiAssembly(
        string name,
        Version version,
        string? cultureName,
        string? publicKeyToken,
        PublicApiModule module,
        string? targetFramework,
        string? targetFrameworkMoniker,
        string? inputIdentity,
        bool usesUpdatedMemorySafetyRules,
        ImmutableArray<PublicApiAttribute> attributes,
        ImmutableArray<PublicApiType> types)
    {
        Name = name;
        Version = version;
        CultureName = cultureName;
        PublicKeyToken = publicKeyToken;
        Module = module;
        TargetFramework = targetFramework;
        TargetFrameworkMoniker = targetFrameworkMoniker;
        InputIdentity = inputIdentity;
        UsesUpdatedMemorySafetyRules = usesUpdatedMemorySafetyRules;
        Attributes = attributes;
        Types = types;
        Scope = inputIdentity ?? FullName + "; " + module.ModuleVersionId.ToString("D", CultureInfo.InvariantCulture);
        _symbols = PublicApiSymbolLinker.Link(this);
        _symbolsByDocumentationId = new Dictionary<string, PublicApiSymbol>(_symbols.Length, StringComparer.Ordinal);
        foreach (var symbol in _symbols)
        {
            // Documentation IDs are unique in assemblies compiled from C#. Otherwise, the first symbol wins.
            _symbolsByDocumentationId.TryAdd(symbol.DocumentationId, symbol);
        }
    }

    /// <summary>Gets the simple name of the assembly.</summary>
    public string Name { get; }

    public Version Version { get; }

    /// <summary>Gets the culture of the assembly, or <see langword="null"/> for a culture-neutral assembly.</summary>
    public string? CultureName { get; }

    /// <summary>Gets the public key token as a lowercase hexadecimal string, or <see langword="null"/> when the assembly is not strong-named.</summary>
    public string? PublicKeyToken { get; }

    /// <summary>Gets the display name of the assembly (e.g. <c>MyLibrary, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null</c>).</summary>
    public string FullName => string.Create(CultureInfo.InvariantCulture, $"{Name}, Version={Version}, Culture={CultureName ?? "neutral"}, PublicKeyToken={PublicKeyToken ?? "null"}");

    public PublicApiModule Module { get; }

    /// <summary>
    /// Gets the target framework of the assembly. It is the value provided by <see cref="PublicApiReadOptions.TargetFramework"/>, unchanged,
    /// or the short name inferred from the <c>TargetFrameworkAttribute</c> of the assembly (e.g. <c>net8.0</c>). It is <see langword="null"/> when it is unknown.
    /// </summary>
    public string? TargetFramework { get; }

    /// <summary>Gets the value of the <c>TargetFrameworkAttribute</c> of the assembly (e.g. <c>.NETCoreApp,Version=v8.0</c>), or <see langword="null"/> when the assembly does not have it.</summary>
    public string? TargetFrameworkMoniker { get; }

    /// <summary>Gets the identity of the input provided by <see cref="PublicApiReadOptions.InputIdentity"/>.</summary>
    public string? InputIdentity { get; }

    /// <summary>
    /// Gets the scope of the symbols of this assembly, used by <see cref="PublicApiSymbol.Identity"/>.
    /// It is <see cref="InputIdentity"/> when provided. Otherwise, it is made of the assembly display name and of the module version ID,
    /// so it changes when the assembly is rebuilt with different inputs. It never contains a file path.
    /// </summary>
    public string Scope { get; }

    /// <summary>
    /// Gets a value indicating whether the assembly is compiled with the updated memory safety rules.
    /// In this case, <see cref="PublicApiMember.RequiresUnsafe"/> reports the members the compiler marked as requiring an unsafe context.
    /// </summary>
    public bool UsesUpdatedMemorySafetyRules { get; }

    /// <summary>Gets the custom attributes applied to the assembly.</summary>
    public ImmutableArray<PublicApiAttribute> Attributes { get; }

    /// <summary>Gets the top-level types, sorted by namespace and metadata name. Nested types are available from <see cref="PublicApiType.NestedTypes"/> and <see cref="GetAllTypes"/>.</summary>
    public ImmutableArray<PublicApiType> Types { get; }

    /// <summary>Enumerates all the types, including nested types. A nested type is enumerated after its containing type.</summary>
    public IEnumerable<PublicApiType> GetAllTypes() => Types.SelectMany(static type => type.GetTypeAndNestedTypes());

    /// <summary>Gets all the symbols: types, members, accessors and delegate <c>Invoke</c> methods. A symbol is listed after its declaring type.</summary>
    public ImmutableArray<PublicApiSymbol> GetAllSymbols() => _symbols;

    /// <summary>Finds a type, a member or an accessor using its XML documentation ID (e.g. <c>M:MyNamespace.MyType.MyMethod(System.Int32)</c>).</summary>
    /// <returns>The symbol, or <see langword="null"/> when the assembly does not declare it.</returns>
    public PublicApiSymbol? FindSymbolByDocumentationId(string documentationId)
    {
        ArgumentNullException.ThrowIfNull(documentationId);
        return _symbolsByDocumentationId.GetValueOrDefault(documentationId);
    }

    /// <summary>Finds a type, including nested types, using its metadata full name (e.g. <c>MyNamespace.MyType`1+NestedType</c>).</summary>
    /// <returns>The type, or <see langword="null"/> when the assembly does not declare it.</returns>
    public PublicApiType? FindType(string fullName)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        if (_typesByFullName is null)
        {
            var typesByFullName = new Dictionary<string, PublicApiType>(StringComparer.Ordinal);
            foreach (var type in GetAllTypes())
            {
                typesByFullName.TryAdd(type.FullName, type);
            }

            _typesByFullName = typesByFullName;
        }

        return _typesByFullName.GetValueOrDefault(fullName);
    }

    // The input is a module without an assembly manifest
    internal bool IsModuleOnly { get; init; }

    public override string ToString() => FullName;
}
