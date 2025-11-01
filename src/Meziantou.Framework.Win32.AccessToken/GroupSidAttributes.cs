namespace Meziantou.Framework.Win32;

/// <summary>Specifies attributes for a group SID in an access token.</summary>
[Flags]
[SuppressMessage("Usage", "CA2217:Do not mark enums with FlagsAttribute", Justification = "Values from Windows definition")]
public enum GroupSidAttributes : uint
{
    /// <summary>The SID is mandatory and cannot be disabled.</summary>
    SE_GROUP_MANDATORY = 0x00000001,

    /// <summary>The SID is enabled by default.</summary>
    SE_GROUP_ENABLED_BY_DEFAULT = 0x00000002,

    /// <summary>The SID is enabled for access checks.</summary>
    SE_GROUP_ENABLED = 0x00000004,

    /// <summary>The SID identifies a group account that is the owner of the object.</summary>
    SE_GROUP_OWNER = 0x00000008,

    /// <summary>The SID is used for deny-only access checks.</summary>
    SE_GROUP_USE_FOR_DENY_ONLY = 0x00000010,

    /// <summary>The SID is an integrity SID.</summary>
    SE_GROUP_INTEGRITY = 0x00000020,

    /// <summary>The SID is an integrity SID that is enabled for access checks.</summary>
    SE_GROUP_INTEGRITY_ENABLED = 0x00000040,

    /// <summary>The SID is the logon SID that identifies the logon session.</summary>
    SE_GROUP_LOGON_ID = 0xC0000000,

    /// <summary>The SID identifies a domain-local group.</summary>
    SE_GROUP_RESOURCE = 0x20000000,

    /// <summary>Mask of valid attribute bits.</summary>
    SE_GROUP_VALID_ATTRIBUTES = 0xE000007F,
}
