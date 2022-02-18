namespace Meziantou.Framework.CodeDom;

public interface IParametrableType
{
    CodeObjectCollection<TypeParameter> Parameters { get; }
}
