using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.TaggedValues.Analyzer;

/// <summary>
/// Identifies the declaration a code fix edits.
/// </summary>
internal static class ValueTagTargetKind
{
    public const string Symbol = "Symbol";
    public const string ReturnValue = "ReturnValue";
    public const string Local = "Local";

    /// <summary>
    /// The property of a record primary constructor parameter, which is tagged by the attributes of the parameter or by its <c>[property: ...]</c> attributes.
    /// </summary>
    public const string RecordProperty = "RecordProperty";

    /// <summary>
    /// Returns <see cref="RecordProperty"/> instead of <see cref="Symbol"/> when <paramref name="target"/> is the property of a record primary constructor parameter,
    /// as the property and the parameter share their declaration.
    /// </summary>
    public static string ForSymbol(ISymbol target, string targetKind)
    {
        return targetKind is Symbol && TagResolver.IsRecordPrimaryConstructorProperty(target) ? RecordProperty : targetKind;
    }
}
