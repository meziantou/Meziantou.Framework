namespace Meziantou.Framework.Win32;

/// <summary>Specifies additional information about the source of a file or directory change.</summary>
[Flags]
public enum SourceInformation
{
    /// <summary>No source information is specified.</summary>
    SourceInfoNotSpecified = 0x0000000,

    /// <summary>
    /// The source (application) has not modified the external view of
    /// the file or directory. For example, Windows 2000 provides a
    /// service that transparently moves unused files to tape and restores
    /// them if access is attempted. Records will be added when the file is
    /// removed or restored, but they can be ignored since the change does
    /// not affect what an application reads from the file.
    /// </summary>
    DataManagement = 0x00000001,

    /// <summary>
    /// The source has not modified the external view of the file
    /// with regard to the application that created this file. For
    /// example, a virus program can specify this when cleaning a
    /// document, or a thumbnail viewer might store preview data
    /// in a private named stream.
    /// </summary>
    AuxiliaryData = 0x00000002,

    /// <summary>
    /// The source is modifying the file to match the contents of
    /// the same file, which exists in another member of the replica set.
    /// </summary>
    ReplicationManagement = 0x00000004,

    /// <summary>The operation is modifying a file on client systems to match the contents of the same file that exists in the cloud.</summary>
    ClientReplicationManagement = 0x00000008,
}
