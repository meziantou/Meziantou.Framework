namespace Meziantou.Framework.CodeDom
{
    public interface ICustomAttributeContainer
    {
        CodeObjectCollection<CodeCustomAttribute> CustomAttributes { get; }
    }
}