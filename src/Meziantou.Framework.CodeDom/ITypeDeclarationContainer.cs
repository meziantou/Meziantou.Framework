namespace Meziantou.Framework.CodeDom;

/// <summary>Defines an interface for code objects that can contain type declarations.</summary>
public interface ITypeDeclarationContainer
{
    /// <summary>Gets the collection of type declarations.</summary>
    CodeObjectCollection<TypeDeclaration> Types { get; }
}
