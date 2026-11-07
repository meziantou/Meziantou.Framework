namespace Meziantou.Framework.Fonts;

/// <summary>What an operation of the display list of a color glyph does.</summary>
public enum ColorOpKind
{
    /// <summary>Fills the outline of a glyph with a paint, over what is already drawn, within the clip.</summary>
    FillGlyph,

    /// <summary>Fills the whole clip with a paint, over what is already drawn.</summary>
    FillClip,

    /// <summary>Draws the image of the glyph, over what is already drawn, within the clip.</summary>
    FillImage,

    /// <summary>Restricts the next operations to the outline of a glyph, within the clip.</summary>
    PushClipGlyph,

    /// <summary>Restricts the next operations to a box, within the clip.</summary>
    PushClipBox,

    /// <summary>Ends the last restriction.</summary>
    PopClip,

    /// <summary>Starts an empty layer the next operations draw on.</summary>
    PushLayer,

    /// <summary>Ends the last layer: it is composed with the layer under it, with a composition mode.</summary>
    PopLayer,
}
