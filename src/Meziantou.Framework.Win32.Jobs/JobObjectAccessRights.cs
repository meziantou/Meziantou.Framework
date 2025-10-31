namespace Meziantou.Framework.Win32;

/// <summary>Defines the access rights for job objects.</summary>
[Flags]
public enum JobObjectAccessRights
{
    /// <summary>Required to delete the object.</summary>
    Delete = 0x00010000,

    /// <summary>Required to read information in the security descriptor for the object, not including the information in the SACL.</summary>
    ReadControl = 0x00020000,

    /// <summary>Required to modify the DACL in the security descriptor for the object.</summary>
    WriteDAC = 0x00040000,

    /// <summary>Required to change the owner in the security descriptor for the object.</summary>
    WriteOwner = 0x00080000,

    /// <summary>Required to wait for the job object to terminate.</summary>
    Synchronize = 0x00100000,

    /// <summary>Required to call the AssignProcessToJobObject function to assign processes to the job object.</summary>
    AssignProcess = 0x00000001,

    /// <summary>Required to call the SetInformationJobObject function to set the attributes of the job object.</summary>
    SetAttributes = 0x00000002,

    /// <summary>Required to retrieve certain information about a job object, such as attributes and accounting information.</summary>
    Query = 0x00000004,

    /// <summary>Required to call the TerminateJobObject function to terminate all processes in the job object.</summary>
    Terminate = 0x00000008,

    /// <summary>Required to call the SetInformationJobObject function to set security-related attributes of the job object.</summary>
    SetSecurityAttributes = 0x00000010,

    /// <summary>Required to call the ImpersonateLoggedOnUser function to impersonate a user who is logged on.</summary>
    Impersonate = 0x00000020,

    /// <summary>Combines all standard, specific, and job object access rights.</summary>
    AllAccess = 0x001f003f,
}
