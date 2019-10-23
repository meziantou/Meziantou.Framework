namespace Meziantou.Framework.CodeDom
{
    public interface IUsingDirectiveContainer
    {
        CodeObjectCollection<UsingDirective> Usings { get; }
    }
}
