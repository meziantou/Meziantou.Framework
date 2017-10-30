namespace Meziantou.Framework.CodeDom
{
    internal interface IInheritanceParameters
    {
        CodeTypeReference BaseType { get; set; }
        CodeObjectCollection<CodeTypeReference> Implements { get; }
    }
}