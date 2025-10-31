namespace Meziantou.Framework.Win32;

/// <summary>Defines security limitations for processes in a job object.</summary>
[Flags]
public enum JobObjectSecurityLimit
{
    /// <summary>Prevents any process in the job from using a token that specifies the local administrators group.</summary>
    NoAdmin = 0x00000001,

    /// <summary>Forces processes in the job to run under a restricted token.</summary>
    RestrictedToken = 0x00000002,
}
