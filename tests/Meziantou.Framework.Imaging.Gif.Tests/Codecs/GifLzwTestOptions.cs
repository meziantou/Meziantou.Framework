namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>Options of <see cref="GifLzwTestEncoder"/>.</summary>
internal sealed record GifLzwTestOptions
{
    /// <summary>Gets the number of clear codes written before the first data code (0: the stream starts without clear code).</summary>
    public int LeadingClearCodes { get; init; } = 1;

    /// <summary>Gets a value indicating whether a full table (4096 codes) is followed by a clear code; otherwise 12-bit codes continue without new entries (deferred clear).</summary>
    public bool ClearWhenFull { get; init; } = true;

    /// <summary>Gets the number of table entries after which a clear code is written (0: never, except when the table is full).</summary>
    public int ClearInterval { get; init; }

    /// <summary>Gets a value indicating whether the end code is written.</summary>
    public bool EndCode { get; init; } = true;

    /// <summary>Gets raw codes written (with the current code size) after the end code: data that decoders must ignore.</summary>
    public IReadOnlyList<int> CodesAfterEnd { get; init; } = [];
}
