namespace Meziantou.Framework;
public static class EnvironmentExtensions
{
    extension(Environment)
    {
        public static string GetRequiredEnvironmentVariable(string variableName)
        {
            var value = Environment.GetEnvironmentVariable(variableName);
            return value ?? throw new InvalidOperationException($"Environment variable '{variableName}' not set");
        }

        public static string GetEnvironmentVariableOrDefault(string variableName, string defaultValue)
        {
            var value = Environment.GetEnvironmentVariable(variableName);
            return value ?? defaultValue;
        }
    }
}
