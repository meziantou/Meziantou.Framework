namespace Meziantou.Framework.PostgreSql.Handler;

/// <summary>Authentication methods supported by the PostgreSQL server.</summary>
public enum PostgreSqlAuthenticationMethod
{
    /// <summary>Cleartext password authentication.</summary>
    ClearTextPassword,

    /// <summary>MD5 password authentication.</summary>
    Md5Password,

    /// <summary>SCRAM-SHA-256 authentication.</summary>
    ScramSha256,
}
