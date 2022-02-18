namespace Meziantou.Framework.CodeDom;

public interface ICustomAttributeContainer
{
    CodeObjectCollection<CustomAttribute> CustomAttributes { get; }
}
