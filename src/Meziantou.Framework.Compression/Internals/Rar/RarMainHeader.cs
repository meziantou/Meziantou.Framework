namespace Meziantou.Framework.Compression.Internals.Rar;

internal sealed class RarMainHeader : RarHeader
{
    public bool IsVolume { get; init; }

    public bool IsSolid { get; init; }

    public bool IsFirstVolume { get; init; }

    public bool NewVolumeNaming { get; init; }

    public bool HeadersEncrypted { get; init; }

    /// <summary>Gets or sets the archive comment of RAR 2.x, stored in the main header.</summary>
    public string? OldComment { get; set; }
}
