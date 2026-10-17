using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

// Sets the relationships between the symbols of an assembly once they are all created, and computes their documentation IDs
internal static class PublicApiSymbolLinker
{
    public static ImmutableArray<PublicApiSymbol> Link(PublicApiAssembly assembly)
    {
        var symbols = ImmutableArray.CreateBuilder<PublicApiSymbol>();
        foreach (var type in assembly.Types)
        {
            LinkType(assembly, type, declaringType: null, symbols);
        }

        return symbols.ToImmutable();
    }

    private static void LinkType(PublicApiAssembly assembly, PublicApiType type, PublicApiType? declaringType, ImmutableArray<PublicApiSymbol>.Builder symbols)
    {
        LinkSymbol(assembly, type, declaringType, symbols);
        if (type.DelegateInvokeMethod is not null)
        {
            LinkSymbol(assembly, type.DelegateInvokeMethod, type, symbols);
        }

        foreach (var member in type.Members)
        {
            LinkSymbol(assembly, member, type, symbols);
            switch (member)
            {
                case PublicApiProperty property:
                    LinkAccessor(assembly, property.GetMethod, type, symbols);
                    LinkAccessor(assembly, property.SetMethod, type, symbols);
                    break;

                case PublicApiEvent @event:
                    LinkAccessor(assembly, @event.AddMethod, type, symbols);
                    LinkAccessor(assembly, @event.RemoveMethod, type, symbols);
                    LinkAccessor(assembly, @event.RaiseMethod, type, symbols);
                    break;
            }
        }

        foreach (var nestedType in type.NestedTypes)
        {
            LinkType(assembly, nestedType, type, symbols);
        }
    }

    private static void LinkAccessor(PublicApiAssembly assembly, PublicApiMethod? accessor, PublicApiType declaringType, ImmutableArray<PublicApiSymbol>.Builder symbols)
    {
        if (accessor is not null)
        {
            LinkSymbol(assembly, accessor, declaringType, symbols);
        }
    }

    private static void LinkSymbol(PublicApiAssembly assembly, PublicApiSymbol symbol, PublicApiType? declaringType, ImmutableArray<PublicApiSymbol>.Builder symbols)
    {
        symbol.Assembly = assembly;
        symbol.DeclaringType = declaringType;
        symbol.DocumentationId = DocumentationIdBuilder.GetDocumentationId(symbol);
        symbols.Add(symbol);
    }
}
