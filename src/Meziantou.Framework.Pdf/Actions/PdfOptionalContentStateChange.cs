namespace Meziantou.Framework.Pdf;

/// <summary>The change a set-OCG-state action makes to optional content groups (ISO 32000-2, 12.6.4.13).</summary>
public enum PdfOptionalContentStateChange
{
    /// <summary>The groups are turned on (<c>ON</c>).</summary>
    On,

    /// <summary>The groups are turned off (<c>OFF</c>).</summary>
    Off,

    /// <summary>The state of each group is inverted (<c>Toggle</c>).</summary>
    Toggle,
}
