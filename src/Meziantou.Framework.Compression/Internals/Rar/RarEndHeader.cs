namespace Meziantou.Framework.Compression.Internals.Rar;

internal sealed class RarEndHeader : RarHeader
{
    public bool HasNextVolume { get; init; }
}
