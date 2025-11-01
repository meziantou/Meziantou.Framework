namespace Meziantou.Framework.CodeDom;

/// <summary>Defines an interface for code objects that can contain namespace declarations.</summary>
public interface INamespaceDeclarationContainer
{
    /// <summary>Gets the collection of namespace declarations.</summary>
    CodeObjectCollection<NamespaceDeclaration> Namespaces { get; }
}
