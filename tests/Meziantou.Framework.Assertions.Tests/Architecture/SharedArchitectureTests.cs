using SampleApp.Domain;

namespace Meziantou.Framework.Assertions.Architecture.Tests;

public sealed class SharedArchitectureTests
{
    #region Architecture_SharedArchitecture
    // Loading reads the assemblies: load them once and share the model between the tests
    private static readonly ArchitectureModel Architecture = ArchitectureModel.FromAssemblyContaining<Order>();

    [Fact]
    public void DomainDoesNotDependOnInfrastructure() =>
        Assert.Architecture.DoesNotDependOn(
            Architecture.Types.Where(t => t.IsInNamespaceOrChildren("SampleApp.Domain")),
            t => t.IsInNamespaceOrChildren("SampleApp.Infrastructure"));

    [Fact]
    public void RepositoriesAreSealed() =>
        Assert.All(Architecture.Types.Where(t => t.IsClass && t.Implements<IOrderRepository>()), t => t.IsSealed);
    #endregion
}
