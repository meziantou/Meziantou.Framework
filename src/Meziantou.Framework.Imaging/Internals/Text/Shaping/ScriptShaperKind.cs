namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The shaping model of a script: what a text needs besides the features every script has.</summary>
internal enum ScriptShaperKind
{
    /// <summary>No analysis of the text: the features of the font do the work.</summary>
    Default,

    /// <summary>Letters that join: Arabic and Syriac.</summary>
    Arabic,

    /// <summary>Hebrew: the order of its points, and the precomposed letters of fonts that cannot position them.</summary>
    Hebrew,
}
