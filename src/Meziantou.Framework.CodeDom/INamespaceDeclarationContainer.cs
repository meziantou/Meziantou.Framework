namespace Meziantou.Framework.CodeDom
{
    public interface INamespaceDeclarationContainer
    {
        CodeObjectCollection<NamespaceDeclaration> Namespaces { get; }
    }
}