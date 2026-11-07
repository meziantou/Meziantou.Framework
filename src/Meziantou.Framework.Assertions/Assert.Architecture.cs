using Meziantou.Framework.Assertions.Architecture;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Gets the assertions on the dependencies of the types of an <see cref="ArchitectureModel"/>, such as <c>Assert.Architecture.DoesNotDependOn(domain, infrastructure)</c>.</summary>
    public static ArchitectureAssert Architecture => ArchitectureAssert.Instance;
}
