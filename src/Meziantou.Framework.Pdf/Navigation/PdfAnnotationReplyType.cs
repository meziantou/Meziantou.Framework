namespace Meziantou.Framework.Pdf;

/// <summary>The relationship between an annotation and the annotation it is in reply to (<c>/RT</c>, ISO 32000-2, 12.5.6.2, table 172).</summary>
public enum PdfAnnotationReplyType
{
    /// <summary>The annotation is a reply to the other one (<c>R</c>).</summary>
    Reply,

    /// <summary>The annotation is grouped with the other one, which represents the group (<c>Group</c>).</summary>
    Group,
}
