using Meziantou.Framework.Assertions.Architecture;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Gets the assertions on the types and the members of an <see cref="ArchitectureModel"/>, such as <c>Assert.Architecture.DoesNotDependOn(domain, infrastructure)</c>.</summary>
    public static ArchitectureAssert Architecture => ArchitectureAssert.Instance;
}
