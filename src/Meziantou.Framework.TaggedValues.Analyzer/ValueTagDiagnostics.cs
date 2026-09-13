namespace Meziantou.Framework.TaggedValues.Analyzer;

internal static class ValueTagDiagnostics
{
    internal const string ComparedValuesDiagnosticId = "MFTV0001";
    internal const string FlowMismatchDiagnosticId = "MFTV0002";
    internal const string CombinedValuesDiagnosticId = "MFTV0003";
    internal const string InheritedTagMismatchDiagnosticId = "MFTV0004";
    internal const string InvalidAnnotationDiagnosticId = "MFTV0005";
    internal const string AmbiguousConventionDiagnosticId = "MFTV0006";
    internal const string RedundantTagDiagnosticId = "MFTV0007";
    internal const string MissingReturnTagDiagnosticId = "MFTV0008";
    internal const string UntaggedValueDiagnosticId = "MFTV0009";

    /// <summary>The diagnostic property holding the tags a code fix writes, see <see cref="TagInfo.Serialize"/>.</summary>
    internal const string TagsProperty = "Tags";

    /// <summary>The diagnostic property holding what a code fix edits, see <see cref="ValueTagTargetKind"/>.</summary>
    internal const string TargetKindProperty = "TargetKind";

    /// <summary>The diagnostic property set when a code fix can remove the annotation located by the diagnostic.</summary>
    internal const string RemovableProperty = "Removable";

    /// <summary>The name of the <c>.editorconfig</c> option that enables naming-convention inference.</summary>
    internal const string InferTagsFromNamesOption = "taggedvalues.infer_tags_from_names";

    /// <summary>The name of the <c>.editorconfig</c> option that reports tagged values mixed with untagged values.</summary>
    internal const string StrictOption = "taggedvalues.strict";
}
