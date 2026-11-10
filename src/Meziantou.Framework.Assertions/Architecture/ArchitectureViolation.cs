using System.Text;

namespace Meziantou.Framework.Assertions.Architecture;

// A line of the message of a failed assertion: the description of the violation, then one line per dependency that causes it
internal sealed class ArchitectureViolation(string message, IReadOnlyList<Dependency> dependencies)
{
    public string Message { get; } = message;

    public IReadOnlyList<Dependency> Dependencies { get; } = dependencies;

    public override string ToString()
    {
        var sb = new StringBuilder(Message);
        foreach (var dependency in Dependencies)
        {
            sb.Append("\n  ").Append(dependency);
        }

        return sb.ToString();
    }
}
