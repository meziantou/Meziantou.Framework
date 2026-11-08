namespace Meziantou.Framework.Tds.Protocol;

internal sealed class TdsLoginRequest
{
    public string? UserName { get; init; }

    public string? Password { get; init; }

    public string? ApplicationName { get; init; }

    public string? Database { get; init; }

    public byte[] Sspi { get; init; } = [];

    /// <summary>Gets a value indicating whether the client asked for integrated security (fIntSecurity).</summary>
    public bool IsIntegratedSecurity { get; init; }

    public string? AuthenticationToken =>
        Sspi.Length > 0 ? Convert.ToBase64String(Sspi) : null;
}
