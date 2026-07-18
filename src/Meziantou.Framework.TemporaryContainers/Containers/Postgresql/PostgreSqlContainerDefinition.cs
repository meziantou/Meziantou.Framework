namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A <see cref="ContainerDefinition"/> pre-configured for PostgreSQL. Create one with <see cref="ContainerDefinitionPostgreSqlExtensions.CreatePostgreSql()"/>.</summary>
public sealed class PostgreSqlContainerDefinition : ContainerDefinition
{
    internal PostgreSqlContainerDefinition(ImageSource image)
        : base(image)
    {
    }

    /// <summary>Creates a <see cref="PostgreSqlContainer"/> from a deep copy of this definition.</summary>
    /// <returns>A new PostgreSQL container.</returns>
    public override PostgreSqlContainer CreateContainer()
    {
        return new PostgreSqlContainer(new ContainerDefinition(this));
    }
}
