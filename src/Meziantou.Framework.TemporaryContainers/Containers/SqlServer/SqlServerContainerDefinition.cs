namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A <see cref="ContainerDefinition"/> pre-configured for SQL Server. Create one with <see cref="ContainerDefinitionSqlServerExtensions.CreateSqlServer()"/>.</summary>
public sealed class SqlServerContainerDefinition : ContainerDefinition
{
    private const int DefaultPasswordLength = 24;

    internal SqlServerContainerDefinition(ImageSource image)
        : base(image)
    {
        SaPassword = GenerateStrongPassword();
    }

    /// <summary>Gets or sets the SQL Server SA password. Setting this value updates <c>MSSQL_SA_PASSWORD</c> and <c>SA_PASSWORD</c> in <see cref="ContainerDefinition.Environment"/>.</summary>
    public string SaPassword
    {
        get
        {
            return Environment.GetValue("MSSQL_SA_PASSWORD") ?? Environment.GetValue("SA_PASSWORD") ?? throw new InvalidOperationException("The SA password is not configured.");
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Environment.Add("MSSQL_SA_PASSWORD", value);
            Environment.Add("SA_PASSWORD", value);
        }
    }

    /// <summary>Creates a <see cref="SqlServerContainer"/> from a deep copy of this definition.</summary>
    /// <returns>A new SQL Server container.</returns>
    public override SqlServerContainer CreateContainer()
    {
        return new SqlServerContainer(new ContainerDefinition(this));
    }

    private static string GenerateStrongPassword()
    {
        return ContainerCredentialGenerator.GenerateStrongPassword(DefaultPasswordLength);
    }
}
