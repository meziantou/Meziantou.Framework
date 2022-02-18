namespace Meziantou.Framework.CodeDom;

public interface ITypeDeclarationContainer
{
    CodeObjectCollection<TypeDeclaration> Types { get; }
}
