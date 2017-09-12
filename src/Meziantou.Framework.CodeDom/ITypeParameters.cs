namespace Meziantou.Framework.CodeDom
{
    public interface ITypeParameters
    {
        CodeObjectCollection<CodeTypeReference> Parameters { get; }
    }
}