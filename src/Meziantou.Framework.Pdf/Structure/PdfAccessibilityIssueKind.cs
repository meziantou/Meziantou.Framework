namespace Meziantou.Framework.Pdf;

/// <summary>The accessibility issues <see cref="PdfAccessibilityInspector"/> reports, from the requirements of PDF/UA (ISO 14289) and Tagged PDF (ISO 32000-2, 14.8).</summary>
public enum PdfAccessibilityIssueKind
{
    /// <summary>The document has no structure tree (<c>/StructTreeRoot</c>): it is not tagged.</summary>
    MissingStructureTree,

    /// <summary>The document does not declare that it is tagged (<c>/MarkInfo</c> with <c>/Marked true</c>).</summary>
    NotMarkedAsTagged,

    /// <summary>The document has no natural language (<c>/Lang</c> of the catalog).</summary>
    MissingLanguage,

    /// <summary>The document has no title (<c>dc:title</c> of the XMP metadata, or <c>/Title</c> of the document information).</summary>
    MissingTitle,

    /// <summary>The viewer shows the file name instead of the title (<c>/DisplayDocTitle</c> of the viewer preferences is not true).</summary>
    TitleNotDisplayed,

    /// <summary>Content of a page is neither tagged (in a marked-content sequence of the structure tree) nor an artifact.</summary>
    UntaggedContent,

    /// <summary>A figure (<c>Figure</c>, <c>Formula</c>) has no alternate description (<c>/Alt</c>) nor replacement text.</summary>
    MissingAlternateText,

    /// <summary>An annotation of a page is not in the structure tree (it has no <c>/StructParent</c>).</summary>
    UntaggedAnnotation,

    /// <summary>A page with annotations does not order them by the structure (<c>/Tabs /S</c>).</summary>
    MissingTabOrder,

    /// <summary>The structure tree does not read the content of a page in the order the content draws it.</summary>
    ReadingOrder,

    /// <summary>A structure type is neither a standard type nor mapped to one by the role map.</summary>
    UnknownStructureType,

    /// <summary>A heading level is skipped, such as an <c>H3</c> after an <c>H1</c>.</summary>
    SkippedHeadingLevel,
}
