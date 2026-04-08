using System.Collections.Generic;

namespace Meziantou.Framework;

/// <summary>Builder for configuring environment variable modifications.</summary>
public sealed class ProcessWrapperEnvironmentVariables
{
    private readonly IDictionary<string, string?> _environment;

    public ProcessWrapperEnvironmentVariables()
        : this(new Dictionary<string, string?>(StringComparer.Ordinal))
    {
    }

    internal ProcessWrapperEnvironmentVariables(IDictionary<string, string?> environment)
    {
        _environment = environment;
    }

    /// <summary>Sets the specified environment variable to the given value.</summary>
    public ProcessWrapperEnvironmentVariables Set(string name, string value)
    {
        _environment[name] = value;
        return this;
    }

    /// <summary>Removes the specified environment variable.</summary>
    public ProcessWrapperEnvironmentVariables Remove(string name)
    {
        _environment.Remove(name);
        return this;
    }
}
