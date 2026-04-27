namespace Meziantou.Framework.Tds.Handler;

/// <summary>Represents an error returned from query processing.</summary>
public sealed class TdsQueryError
{
    /// <summary>Gets or sets the SQL Server-style error number.</summary>
    public uint Number { get; set; } = 50000;

    /// <summary>Gets or sets the error state.</summary>
    public byte State { get; set; } = 1;

    /// <summary>Gets or sets the error class/severity.</summary>
    public byte Class { get; set; } = 16;

    /// <summary>Gets or sets the error message.</summary>
    public required string Message { get; set; }
}
