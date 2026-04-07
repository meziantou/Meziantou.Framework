using System.Collections.Immutable;

namespace Meziantou.Framework;

/// <summary>Builder for configuring environment variable modifications.</summary>
public sealed class ProcessWrapperEnvironmentVariables
{
    internal ImmutableArray<EnvironmentVariableAction> Actions { get; private set; } = [];

    /// <summary>Sets the specified environment variable to the given value.</summary>
    public ProcessWrapperEnvironmentVariables Set(string name, string value)
    {
        Actions = Actions.Add(new EnvironmentVariableAction(name, value));
        return this;
    }

    /// <summary>Removes the specified environment variable.</summary>
    public ProcessWrapperEnvironmentVariables Remove(string name)
    {
        Actions = Actions.Add(new EnvironmentVariableAction(name, Value: null));
        return this;
    }
}
