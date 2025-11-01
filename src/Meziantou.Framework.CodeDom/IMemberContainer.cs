namespace Meziantou.Framework.CodeDom;

/// <summary>Defines an interface for code objects that can contain member declarations.</summary>
public interface IMemberContainer
{
    /// <summary>Gets the collection of member declarations.</summary>
    CodeObjectCollection<MemberDeclaration> Members { get; }
}
