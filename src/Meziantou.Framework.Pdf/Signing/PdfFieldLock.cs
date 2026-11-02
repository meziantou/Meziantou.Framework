namespace Meziantou.Framework.Pdf.Signing;

/// <summary>
/// The fields a signature locks (ISO 32000-2, 12.8.2.4, the FieldMDP transform): their values cannot change after the
/// signature without invalidating it.
/// </summary>
public sealed class PdfFieldLock
{
    /// <summary>Initializes a new instance of the <see cref="PdfFieldLock"/> class.</summary>
    /// <param name="action">The fields the lock applies to.</param>
    /// <param name="fields">The fully qualified names of the fields of <see cref="PdfFieldLockAction.Include"/> and <see cref="PdfFieldLockAction.Exclude"/>.</param>
    /// <exception cref="ArgumentException">The action is <see cref="PdfFieldLockAction.Include"/> or <see cref="PdfFieldLockAction.Exclude"/>, and no field is given.</exception>
    public PdfFieldLock(PdfFieldLockAction action, IEnumerable<string>? fields = null)
    {
        if (!Enum.IsDefined(action))
            throw new ArgumentOutOfRangeException(nameof(action), action, "The action is not valid");

        Action = action;
        Fields = action == PdfFieldLockAction.All ? [] : [.. fields ?? []];
        if (action != PdfFieldLockAction.All && Fields.Count == 0)
            throw new ArgumentException("The fields to include or to exclude are not given", nameof(fields));
    }

    /// <summary>Gets the fields the lock applies to.</summary>
    public PdfFieldLockAction Action { get; }

    /// <summary>Gets the fully qualified names of the fields that are included or excluded; empty for <see cref="PdfFieldLockAction.All"/>.</summary>
    public IReadOnlyList<string> Fields { get; }

    /// <summary>Gets a value indicating whether a field is locked: a field is locked with its descendants.</summary>
    internal bool IsLocked(string fieldName)
    {
        if (Action == PdfFieldLockAction.All)
            return true;

        var listed = false;
        foreach (var field in Fields)
        {
            if (fieldName == field || (fieldName.StartsWith(field, StringComparison.Ordinal) && fieldName.Length > field.Length && fieldName[field.Length] == '.'))
            {
                listed = true;
                break;
            }
        }

        return Action == PdfFieldLockAction.Include ? listed : !listed;
    }
}
