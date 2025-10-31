namespace Meziantou.Framework.CodeDom;

/// <summary>Defines an interface for code objects that can contain using directives.</summary>
public interface IUsingDirectiveContainer
{
    /// <summary>Gets the collection of using directives.</summary>
    CodeObjectCollection<UsingDirective> Usings { get; }
}
