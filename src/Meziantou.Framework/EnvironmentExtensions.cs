namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for <see cref="Environment"/>.
/// </summary>
public static class EnvironmentExtensions
{
    extension(Environment)
    {
        /// <summary>Gets the value of an environment variable, throwing an exception if it doesn't exist.</summary>
        public static string GetRequiredEnvironmentVariable(string variableName)
        {
            var value = Environment.GetEnvironmentVariable(variableName);
            return value ?? throw new InvalidOperationException($"Environment variable '{variableName}' not set");
        }

        /// <summary>Gets the value of an environment variable or returns a default value if it doesn't exist.</summary>
        public static string GetEnvironmentVariableOrDefault(string variableName, string defaultValue)
        {
            var value = Environment.GetEnvironmentVariable(variableName);
            return value ?? defaultValue;
        }
    }
}
