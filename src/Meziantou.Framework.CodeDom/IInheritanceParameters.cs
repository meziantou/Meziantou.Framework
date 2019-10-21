#nullable disable
namespace Meziantou.Framework.CodeDom
{
    internal interface IInheritanceParameters
    {
        TypeReference BaseType { get; set; }
        CodeObjectCollection<TypeReference> Implements { get; }
    }
}
