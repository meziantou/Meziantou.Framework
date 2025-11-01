namespace Meziantou.Framework.CodeDom;

/// <summary>Defines an interface for code objects that can have XML documentation comments.</summary>
public interface IXmlCommentable
{
    /// <summary>Gets the collection of XML documentation comments.</summary>
    XmlCommentCollection XmlComments { get; }
}
