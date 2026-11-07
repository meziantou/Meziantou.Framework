namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Options shared by the wait strategies that check the container repeatedly until it is ready.</summary>
public class PollingWaitStrategyOptions
{
    /// <summary>Gets or sets the delay between two checks. The default value is 250 milliseconds.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Validates the options set by a configuration callback, reporting an invalid value as an invalid <paramref name="paramName"/>.</summary>
    internal virtual void Validate(string paramName)
    {
        if (PollInterval < TimeSpan.Zero)
            throw new ArgumentException($"{nameof(PollInterval)} must not be negative.", paramName);
    }
}
