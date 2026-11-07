namespace Meziantou.Framework.Imaging.Typography;

/// <summary>What happens to a word that is wider than the layout.</summary>
public enum TextOverflowWrap
{
    /// <summary>The word is not broken: its line is wider than the layout.</summary>
    Normal = 0,

    /// <summary>The word is broken between two of its characters, where no other break is possible on the line.</summary>
    Anywhere = 1,
}
