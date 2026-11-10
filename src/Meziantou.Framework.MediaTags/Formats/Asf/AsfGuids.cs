namespace Meziantou.Framework.MediaTags.Formats.Asf;

/// <summary>The objects of an ASF header that hold metadata, and the ones the writer updates.</summary>
/// <remarks>See the Advanced Systems Format specification, revision 01.20.06.</remarks>
internal static class AsfGuids
{
    public static readonly Guid Header = new("75B22630-668E-11CF-A6D9-00AA0062CE6C");
    public static readonly Guid FileProperties = new("8CABDCA1-A947-11CF-8EE4-00C00C205365");
    public static readonly Guid StreamProperties = new("B7DC0791-A9B7-11CF-8EE6-00C00C205365");
    public static readonly Guid ContentDescription = new("75B22633-668E-11CF-A6D9-00AA0062CE6C");
    public static readonly Guid ExtendedContentDescription = new("D2D0A440-E307-11D2-97F0-00A0C95EA850");
    public static readonly Guid HeaderExtension = new("5FBF03B5-A92E-11CF-8EE3-00C00C205365");
    public static readonly Guid HeaderExtensionReserved = new("ABD3D211-A9BA-11CF-8EE6-00C00C205365");
    public static readonly Guid Metadata = new("C5F8CBEA-5BAF-4877-8467-AA8C44FA4CCA");
    public static readonly Guid MetadataLibrary = new("44231C94-9498-49D1-A141-1D134E457054");
    public static readonly Guid VideoMedia = new("BC19EFC0-5B4D-11CF-A8FD-00805F5C442B");
}
