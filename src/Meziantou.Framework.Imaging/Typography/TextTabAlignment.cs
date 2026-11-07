namespace Meziantou.Framework.Imaging.Typography;

/// <summary>How the text after a tabulation is placed at a tabulation stop (<see cref="TextTabStop"/>).</summary>
/// <remarks>
/// The text a stop aligns is the text after the tabulation, up to the next tabulation or to the end of the line (the
/// white space that hangs at the end of a wrapped line excluded). Start and end are the left and the right of a
/// left-to-right paragraph, and the right and the left of a right-to-left one.
/// </remarks>
public enum TextTabAlignment
{
    /// <summary>The text starts at the stop: a left tabulation in a left-to-right paragraph.</summary>
    Start = 0,

    /// <summary>The text ends at the stop: a right tabulation in a left-to-right paragraph.</summary>
    End = 1,

    /// <summary>The text is centered on the stop.</summary>
    Center = 2,

    /// <summary>
    /// The first decimal separator of the text (<see cref="TextTabStop.DecimalSeparator"/>) starts at the stop; a text
    /// without separator ends at the stop, as with <see cref="End"/>.
    /// </summary>
    DecimalSeparator = 3,
}
