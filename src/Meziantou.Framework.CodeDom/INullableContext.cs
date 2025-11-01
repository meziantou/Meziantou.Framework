namespace Meziantou.Framework.CodeDom;

/// <summary>Defines an interface for code objects that have a nullable reference types context.</summary>
public interface INullableContext
{
    /// <summary>Gets the nullable reference types context.</summary>
    NullableContext NullableContext { get; }
}
