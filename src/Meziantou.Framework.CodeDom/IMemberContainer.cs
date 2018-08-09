namespace Meziantou.Framework.CodeDom
{
    public interface IMemberContainer
    {
        CodeObjectCollection<MemberDeclaration> Members { get; }
    }
}