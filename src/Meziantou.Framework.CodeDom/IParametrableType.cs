namespace Meziantou.Framework.CodeDom
{
    public interface IParametrableType
    {
        CodeObjectCollection<CodeTypeParameter> Parameters { get; }
    }
}