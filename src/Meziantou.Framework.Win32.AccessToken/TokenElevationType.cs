namespace Meziantou.Framework.Win32;

/// <summary>Specifies the elevation type of a token in User Account Control (UAC) scenarios.</summary>
public enum TokenElevationType
{
    /// <summary>The token elevation type is unknown or could not be determined.</summary>
    Unknown = 0,

    /// <summary>The token does not have elevation (default token for standard users).</summary>
    Default = 1,

    /// <summary>The token is elevated (has administrator privileges).</summary>
    Full = 2,

    /// <summary>The token is a limited version of an administrator token (UAC filtered token).</summary>
    Limited = 3,
}
