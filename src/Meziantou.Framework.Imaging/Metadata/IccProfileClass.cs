namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>The profile/device class declared in the header of an ICC profile.</summary>
public enum IccProfileClass
{
    /// <summary>The header is missing or too short to declare a class.</summary>
    Unknown = 0,

    /// <summary>Input device, such as a scanner or a camera (<c>'scnr'</c>).</summary>
    Input = 1,

    /// <summary>Display device (<c>'mntr'</c>).</summary>
    Display = 2,

    /// <summary>Output device, such as a printer (<c>'prtr'</c>).</summary>
    Output = 3,

    /// <summary>Device link (<c>'link'</c>).</summary>
    DeviceLink = 4,

    /// <summary>Color space conversion (<c>'spac'</c>).</summary>
    ColorSpace = 5,

    /// <summary>Abstract (<c>'abst'</c>).</summary>
    Abstract = 6,

    /// <summary>Named color (<c>'nmcl'</c>).</summary>
    NamedColor = 7,

    /// <summary>Any other declared class.</summary>
    Other = 8,
}
