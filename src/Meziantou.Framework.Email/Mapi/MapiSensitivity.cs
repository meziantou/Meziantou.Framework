namespace Meziantou.Framework.Email.Mapi;

/// <summary>The sensitivity of an item (PidTagSensitivity).</summary>
public enum MapiSensitivity
{
    /// <summary>Not sensitive.</summary>
    Normal = 0,

    /// <summary>Personal.</summary>
    Personal = 1,

    /// <summary>Private.</summary>
    Private = 2,

    /// <summary>Confidential.</summary>
    Confidential = 3,
}
