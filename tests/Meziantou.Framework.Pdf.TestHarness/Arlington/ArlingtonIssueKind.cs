namespace Meziantou.Framework.Pdf.TestHarness.Arlington;

/// <summary>The kinds of problems of <see cref="ArlingtonValidator"/>.</summary>
public enum ArlingtonIssueKind
{
    /// <summary>A required key or element is missing.</summary>
    MissingKey,

    /// <summary>A value has a type the model does not allow.</summary>
    WrongType,

    /// <summary>A value is not one of the values the model allows.</summary>
    WrongValue,

    /// <summary>A value is direct where it must be indirect, or the reverse.</summary>
    WrongIndirection,

    /// <summary>A key is not in the model (a warning: products may add their own keys).</summary>
    UnknownKey,
}
