namespace Meziantou.Framework.Pdf;

/// <summary>The state of the optional content groups that an optional content configuration does not list (<c>/BaseState</c>, ISO 32000-2, 8.11.4.3).</summary>
public enum PdfOptionalContentBaseState
{
    /// <summary>The groups are on (<c>ON</c>), the default.</summary>
    On,

    /// <summary>The groups are off (<c>OFF</c>).</summary>
    Off,

    /// <summary>The groups keep their current state (<c>Unchanged</c>); not allowed in the default configuration.</summary>
    Unchanged,
}
