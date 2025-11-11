namespace Meziantou.Framework.CodeDom;

/// <summary>Defines an interface for types that support generic type parameters.</summary>
public interface IParametrableType
{
    /// <summary>Gets the collection of generic type parameters.</summary>
    CodeObjectCollection<TypeParameter> Parameters { get; }
}
