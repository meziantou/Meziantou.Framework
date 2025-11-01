namespace Meziantou.Framework.CodeDom;

/// <summary>Defines an interface for code objects that can have custom attributes.</summary>
public interface ICustomAttributeContainer
{
    /// <summary>Gets the collection of custom attributes.</summary>
    CodeObjectCollection<CustomAttribute> CustomAttributes { get; }
}
